# Infrastructure and cross-cutting services

This page covers the helpers and services that many modules share: startup, storage, security, time, email, audit, users, background workers and notifications. Domain services (projects, documents, IPR, Project Office Reports and so on) are listed briefly at the end. Configuration keys are described in [configuration-reference.md](configuration-reference.md).

## Startup and database gate (`Infrastructure/`)

| Type | Purpose |
| --- | --- |
| `DatabaseStartupMigrator` | Runs the startup gate for each EF Core context (`ApplyDeploymentBoundaryAsync`). It applies all pending migrations, which is mandatory: `Database:ApplyMigrationsOnStartup` is ignored. It then runs a validation callback before the app accepts traffic. |
| `MigrationLineageManifest` | Loads `Migrations/immutable-migration-ids.txt` (and the Media Library equivalent) from disk or from the embedded resource. The migrator uses it to detect migration lineage drift. |
| `ApplicationDatabaseSchemaValidator` | Physical schema checks for `ApplicationDbContext` after migration. `Program.cs` also calls `ProjectDocumentSearchVectorMaintenance.ValidateAsync` and some notebook/DocRepo schema checks. |
| `StartupFailureReporter` | Best-effort writer that saves a startup exception to `{Storage:DataRoot or ContentRoot}/startup-diagnostics/startup-failure-*.log`. It never swallows the original exception. |
| `UploadRequestLimitResolver` | Derives the request-body limit for Kestrel, IIS and multipart forms from the largest configured upload limit, plus 8 MiB, with a floor of 32 MiB. |
| `RelationalTransactionScope` | Composable EF Core transaction. The outermost scope owns the transaction; nested scopes use savepoints. `RegisterAfterCommit` callbacks run only after the root commit. |
| `EnforcePasswordChangeFilter` | Global `IAsyncPageFilter` (added through `AddMvcOptions`). An authenticated user with `ApplicationUser.MustChangePassword` is redirected to `/Identity/Account/Manage/ChangePassword`. Login, logout, the change-password page, `/css` and `/js` are exempt. |
| `IdentityPasswordPolicy` | Describes the configured `PasswordOptions` and suggests a length for generated passwords. |
| `IstClock` / `TimeFmt` | IST helpers. `IstClock.TimeZone` comes from `Utilities/TimeZoneHelper.GetIst()`, which calls `TZConvert.GetTimeZoneInfo("Asia/Kolkata")` and works on Windows and Linux. Provides `ToIst(...)` and the day-boundary helpers `StartOfDayIstToUtc` / `ExclusiveEndOfDayIstToUtc`. `TimeFmt.ToIst` formats nullable dates. The zone is fixed and has no configuration key. |
| `SafeCsv` | CSV escaping (including formula-injection safety) and UTF-8-with-BOM output. |
| `Usage/ErpUsageMiddleware` | Pipeline middleware (`app.UseMiddleware<ErpUsageMiddleware>()`) that records ERP activity through `IUserActivityRecorder` (`ErpUsage` options). |
| `Ui/TempDataToastExtensions` | TempData toast helpers for Razor Pages. |
| `Activities/ActivityRepository`, `ActivityTypeRepository` | Activity persistence (`IActivityRepository`). |

Command-line entry points in `Program.cs`:
- `--compendium-offline-self-test` runs `Utilities/Reporting/CompendiumOfflineSelfTest` before the host is built.
- `--backfill-forecast` runs `Services/Scheduling/ForecastBackfillService` after the migration gate, then exits.

## Time

- `Services/IClock` with `SystemClock` (singleton `IClock`) is the injectable UTC clock.
- `Infrastructure/IstClock` and `Utilities/TimeZoneHelper` handle every IST conversion. `TodoService` uses `IstClock.TimeZone`: a date-only due date becomes 23:59:59.9999999 IST before conversion to UTC.

## Storage (`Services/Storage/`, `Services/Security/`)

| Type | Purpose |
| --- | --- |
| `IUploadRootProvider` / `UploadRootProvider` (singleton) | Resolves the shared upload root. Order: env `PM_UPLOAD_ROOT`, then `ProjectPhotos:StorageRoot`. When empty, `Services/Projects/ProjectPhotoOptionsSetup` fills in `{WebRoot}/uploads`, which is normally `wwwroot/uploads`; `/var/pm/uploads` is used only if the value is still empty. It expands environment variables and `~`, creates the directory, and falls back to `%LOCALAPPDATA%/ProjectManagement/uploads` or `{ContentRoot}/uploads` if creation fails. It exposes `RootPath`, `ProjectsRootPath`, `GetProjectRoot`, `GetProjectPhotosRoot`, `GetProjectDocumentsRoot`, `GetProjectCommentsRoot`, `GetProjectVideosRoot` and `GetSocialMediaRoot(prefix, eventId)`. The social-media prefix must contain `{eventId}`. |
| `IUploadPathResolver` / `UploadPathResolver` | Converts between storage keys and absolute paths under the upload root (`ToAbsolute`, `ToRelative`). |
| `IProtectedFileUrlBuilder` / `ProtectedFileUrlBuilder` (scoped) | Builds signed `/files` download and inline URLs. Tokens are bound to the current user when `FileDownload:BindTokensToUser` is set. |
| `IFileAccessTokenService` / `FileAccessTokenService` | Data Protection–protected tokens (purpose `FileAccessTokenService`, lifetime `FileDownload:TokenLifetimeMinutes`). `Controllers/FilesController` (`/files`) serves them. Tokens become invalid if the Data Protection key ring (`DP_KEYS_DIR`) is lost. |
| `FileSystemQuarantine` | Moves files or directories aside before a delete (`StageFile`, `StageDirectory`), with `Restore` and `FinalizeDeletion`, so a failed database transaction can roll the file operation back. |
| `Services/DocRepo/LocalDocStorageService` (`IDocStorage`) | Document Repository storage under `DocRepo:RootPath` (relative paths resolve against ContentRoot). Files are saved as `{yyyy}/{MM}/{guid}.pdf`. |
| `Services/DocRepo/NoopFileScanner` (`IFileScanner`) | The DocRepo scan hook. It does no scanning. |
| `Services/IVirusScanner` | Optional interface accepted by `DocumentService`, `ProjectPhotoService` and `ProjectVideoService`. **No implementation is registered.** If `ProjectDocuments:EnableVirusScan=true`, `DocumentService` throws on every upload. |
| `Utilities/FileNameSanitizer` | Makes user-supplied file names safe. |

The IPR, FFC and ARPP attachment stores (`Application/Ipr/IprAttachmentStorage`, `Application/Ffc/FfcAttachmentStorage`, `Services/Arpp/FileSystemArppAttachmentStorage`) all resolve beneath the upload root unless an explicit `StorageRoot` is configured.

**Data Protection** (`Program.cs`): keys persist to `DP_KEYS_DIR`. When it is not set, they go to `%LOCALAPPDATA%/PRISM-ERP/DataProtectionKeys` in Development and `/var/pm/keys` otherwise. The application name is `ProjectManagement_SDD`. Losing the keys signs everyone out and invalidates outstanding file tokens.

## Email

`Program.cs` registers `Services/SmtpEmailSender` (transient) when `Email:Smtp:Host` is non-empty, otherwise `Services/NoOpEmailSender` (singleton). Both implement ASP.NET Core Identity UI's `IEmailSender`.

- `SmtpEmailSender` reads `Email:*` directly from `IConfiguration`. It always sets `EnableSsl = true`, uses port 25 if the configured port does not parse, and falls back to `Username` and then `no-reply@example.com` for the From address.
- **No application code resolves `IEmailSender` today**, so no mail is sent even when SMTP is configured. Notifications go through SignalR and the database only.

## Audit, users and identity (`Services/`)

| Type | Purpose |
| --- | --- |
| `IAuditService` / `AuditService` | Writes `AuditLogs` entries (action, user, IP, data). Values whose keys look sensitive are scrubbed, and `Todo.*` actions are skipped. |
| `IUserContext` / `HttpUserContext` | Current user id and principal, taken from `IHttpContextAccessor`. |
| `Helpers/ClientIp` | Client IP from `HttpContext.Connection.RemoteIpAddress`. Because forwarded headers are trusted from any source (`KnownNetworks` and `KnownProxies` are cleared), a client can supply this value through `X-Forwarded-For`. |
| `IUserManagementService` / `UserManagementService` | User and role administration over `UserManager` and `RoleManager`. It prevents removing, disabling or demoting the last active Admin and prevents a user disabling their own account. Password resets set `MustChangePassword`. |
| `IUserLifecycleService` / `UserLifecycleService` | Disable, enable, and schedule hard deletes using `UserLifecycle:HardDeleteWindowHours` and `UndoWindowMinutes`. |
| `LoginAnalyticsService` | Data for the admin login scatter chart (percentiles, off-hours and weekend flags). |
| `Data/IdentitySeeder` | Runs only when `Database:RunSeedersOnStartup=true`. It creates the roles in `RoleNames.AssignableRoles` and a bootstrap Admin whose password comes from `Security:BootstrapAdminPassword` or `PRISM_BOOTSTRAP_ADMIN_PASSWORD`. In Development it can also create test users. |
| `Services/Admin/AdminWorkerStatusRegistry` (`IAdminWorkerStatusRegistry`) | Workers register here and report heartbeats, which the admin health pages display. |
| `Services/Admin/AdminSystemHealthService` | Admin health checks: upload root, DP key directory, database, runtime. Its "startup migrations enabled/disabled" text reflects the ignored `Database:ApplyMigrationsOnStartup` value. |
| `Utilities/ConnectionStringHasher` | SHA-256 hex of the connection string, attached to stage and plan decision diagnostics so the raw string is never logged. |

## Background workers (hosted services)

Registered in `Program.cs` unless noted otherwise.

| Worker | Schedule / condition | Configuration |
| --- | --- | --- |
| `Services/UserPurgeWorker` | Every minute | `UserLifecycle` |
| `Services/TodoPurgeWorker` | Periodic | `Todo:RetentionDays` |
| `Services/LoginAggregationWorker` | Every 24 h; aggregates `DailyLoginStats` | none |
| `Services/Usage/UserActivityRetentionWorker` | Periodic | `ErpUsage:RetentionDays` |
| `Services/Projects/ProjectRetentionWorker` | Every 24 h | `Projects:Retention` |
| `Services/Projects/ProjectContentAuditQueue` | In-process queue (also exposed as `IProjectContentAuditQueue`) | none |
| `Services/Notifications/NotificationDispatcher` | Continuous | none |
| `Services/Notifications/NotificationRetentionService` | `SweepInterval` | `Notifications:Retention` |
| `Hosted/AuditRetentionWorker` | Does nothing unless `Audit:Retention:Enabled` | `Audit:Retention` |
| `Hosted/NotebookTrashRetentionWorker` | `SweepInterval` | `Notebook:Trash` |
| `Hosted/DocRepoOcrWorker` | Registered if `DocRepo:EnableOcrWorker` (default true) | `DocRepo` |
| `Hosted/ProjectDocumentOcrWorker` | Registered if `ProjectDocuments:Ocr:EnableWorker` (default true) | `ProjectDocuments:Ocr` |
| `Hosted/OcrTextBackfillWorker` | Registered if `OcrBackfill:Enabled` (default false) | `OcrBackfill` |
| `SearchV2/Indexing/SearchIndexWorker`, `SearchTelemetryRetentionWorker` | Registered by `AddSearchV2` | `Search:V2` |
| `PublicationFontWarmupHostedService`, `PublicationRuntimeValidationHostedService` | Registered by `AddProjectPublications` | fonts (`PRISM_PUBLICATION_FONTS_DIR`) |
| Media Library workers (`PrismMediaOutboxWorker`, `MediaSourceScannerWorker`, `MediaProcessingWorker`, `MediaAvailabilityReconciliationWorker`, `FaceAnalysisQueueWorker`, `FaceCandidateRefreshWorker`, `FaceIdentityGroupingRefreshWorker`) | Registered conditionally by `AddMediaLibrary` | `MediaLibrary` (see the configuration reference) |

The OCR runners (`OcrmypdfDocumentOcrRunner`, `OcrmypdfProjectOcrRunner`, `Services/Ocr/OcrmypdfSharedRunner`) shell out to `ocrmypdf`, either on PATH or at the configured `OcrExecutablePath`. They attempt skip-text, then force-ocr, then redo-ocr.

## Notifications (`Services/Notifications/`, `Hubs/`)

| Type | Purpose |
| --- | --- |
| `NotificationPublisher` | Normalises metadata and writes `NotificationDispatch` rows. |
| `NotificationDispatcher` | Hosted worker. It batches undispatched rows, applies preferences, deduplicates by fingerprint, persists `Notification` rows and pushes them over SignalR. |
| `NotificationRetentionService` | Deletes notifications and dispatches past the configured age or per-user cap. Also deletes completed and dead-letter dispatches. |
| `NotificationPreferenceService` | Per-kind and per-project allow/deny checks and mutes. |
| `RoleNotificationService` | Resolves role members for broadcast notifications. |
| `UserNotificationService` | List, count, mark read/unread and mute, with project-access guards. |
| `Hubs/NotificationsHub` | SignalR hub at `/hubs/notifications`. Unauthenticated API and hub calls get 401/403 instead of a login redirect (`IsApiOrRealtimeRequest`). |

`Services/Remarks/RemarkNotificationService` publishes remark events through this pipeline. It does not send email.

## Navigation and rendering

- `Services/Navigation/RoleBasedNavigationProvider`: role-aware navigation tree.
- `Services/Navigation/UrlBuilder` (`IUrlBuilder`): URL helper.
- `Services/Text/MarkdownRenderer` (`IMarkdownRenderer`): Markdown rendering (used by `Pages/Projects/Overview`).

## Logging

Logging is configured in two places:
- **`Program.cs`** adds a console provider and code-level filters: EF `Database.Command` and `Model.Validation` at Warning, `ProjectManagement.Services.TodoService` at None, `TodoPurgeWorker` at Warning.
- **`appsettings.json`** repeats these filters.

`AuditService` also skips `Todo.*` actions. `Program.cs` logs at startup where the Data Protection keys are stored, the database startup policy, the latest applied migration of each context, and a warning when a non-Development environment connects as `postgres`.

## Domain services (pointers)

These are documented in their module docs. All the types listed here exist:

- **Projects:** `ProjectFactsService`, `ProjectFactsReadService`, `ProjectProcurementReadService`, `ProjectTimelineReadService`, `ProjectCommentService`, `ProjectPhotoService`, `ProjectVideoService`, `Services/Scheduling/ForecastBackfillService`.
- **Documents:** `DocumentService`, `DocumentRequestService`, `DocumentDecisionService`, `DocumentNotificationService`, `DocumentPreviewTokenService`.
- **Remarks:** `RemarkService`, `RemarkNotificationService`, `RemarkMetrics`.
- **Analytics:** `Services/Analytics/ProjectAnalyticsService`.
- **IPR:** `Application/Ipr/IprReadService`, `IprWriteService`, `IprAttachmentStorage`; `Areas/ProjectOfficeReports/Application/IprExportService`.
- **Project Office Reports:** `VisitService`, `VisitPhotoService`, `SocialMediaEventService`, `SocialMediaEventPhotoService`, `ProjectTotTrackerReadService`, the `Proliferation*Service` classes, and the `*ExportService` classes under `Areas/ProjectOfficeReports/Application/`.
