# Configuration reference

Every configuration key, environment variable and command-line switch that the code actually reads, with its real default and the component that uses it. **The code is authoritative**; where `appsettings.json` and the Options class disagree, both values are shown.

- *Code default* = the initializer on the Options class (used when the key is absent from every source).
- *appsettings* = the value shipped in `appsettings.json` (base). Development/Production overrides are called out where they differ.
- Precedence is standard ASP.NET Core: `appsettings.json` < `appsettings.{Environment}.json` < environment variables (`Section__Key`) < command line.
- Options bound with `ValidateOnStart()` stop the application at startup if validation fails. Those are marked **(validated)**.

## Configuration files

| File | Role |
| --- | --- |
| `appsettings.json` | Base settings. Always loaded. |
| `appsettings.Development.json` | Loaded when `ASPNETCORE_ENVIRONMENT=Development` (set by both profiles in `Properties/launchSettings.json`). Uses `D:/ProjectManagementData/...` paths. |
| `appsettings.Production.json` | Loaded when the environment is `Production`. IIS uses `Production` when nothing sets the environment, and `web.config` does not set it. **Apart from six paths (`D:/` becomes `F:/`), this file is identical to `appsettings.Development.json`**, including the `postgres` superuser connection string, `Include Error Detail=true`, and a Windows `OcrExecutablePath`. |
| `appsettings.CompendiumPdf.fragment.json` | **The app never loads this file.** It is a snippet to paste into a real appsettings file. Its values match the `CompendiumPdfOptions` code defaults. |
| `web.config` | IIS in-process hosting (`AspNetCoreModuleV2`, `.\ProjectManagement.exe`). `requestLimits maxAllowedContentLength=268435456` (256 MiB) is the IIS ceiling. stdout logging is off. It sets no environment variables. |
| `Properties/launchSettings.json` | Local profiles only: `https://localhost:7183;http://localhost:7130` (Kestrel) and IIS Express `http://localhost:26273` / SSL 44319. |

Design-time EF tools (`Data/ApplicationDbContextFactory`, `Features/MediaLibrary/Data/MediaLibraryDbContextFactory`) load `appsettings.json` plus `appsettings.{ASPNETCORE_ENVIRONMENT}.json`. If `ASPNETCORE_ENVIRONMENT` is not set, they use `Development`.

---

## Core / host

| Key | Code default | appsettings | Used by / notes |
| --- | --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | none (required) | base: `Host=localhost;Port=5432;Database=ProjectManagement5;Username=postgres;Password=postgres;Include Error Detail=true`. Dev/Prod: same but `Database=ProjectManagement` | `Program.cs` → `ApplicationDbContext` and `MediaLibraryDbContext` (the media context uses migrations history table `MediaLibraryDbContext.MigrationsHistoryTable`). If the configured value is empty, `Program.cs` falls back to the env vars `ConnectionStrings__DefaultConnection`, then `DefaultConnection`, and otherwise throws. Outside Development, a `postgres` username logs a warning at startup. |
| `Database:ApplyMigrationsOnStartup` | n/a | base `false`, Dev/Prod `true` | **Ignored.** `Program.cs` hard-codes `applyMigrationsOnStartup = true`. Migrations for both DbContexts always run at startup and are checked against `Migrations/immutable-migration-ids.txt` and `Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt`. A value of `false` only logs a deprecation warning. `AdminSystemHealthService` still displays this value, so the health page can misleadingly say "disabled". |
| `Database:RunSeedersOnStartup` | `false` | `false` everywhere | When `true`, startup runs `IsoCountrySeeder`, `StageFlowSeeder` and `IdentitySeeder`. |
| `Security:BootstrapAdminUserName` | `admin` | absent | `Data/IdentitySeeder`: the name of the bootstrap administrator. |
| `Security:BootstrapAdminPassword` | none | absent | `IdentitySeeder`. It falls back to env `PRISM_BOOTSTRAP_ADMIN_PASSWORD`. If seeders run, no admin user exists, and neither value is set, startup throws. The created account has `MustChangePassword = true`. |
| `DevelopmentSeedUsers:TestHoD:Password`, `DevelopmentSeedUsers:TestProjectOfficer:Password` | none | absent | `IdentitySeeder`, Development only. Creates `test_hod` / `test_project_offr` when set. |
| `App:Version` | `"1.0"` | absent | `Pages/Developer.cshtml.cs`. |
| `Logging:LogLevel:*` | framework | base sets `Default=Information`, `Microsoft=Warning`, EF Core categories `Warning`, `ProjectManagement.Services.TodoService=None`, `ProjectManagement.Services.TodoPurgeWorker=Warning`. Dev/Prod add `Microsoft.AspNetCore=Warning`. | `Program.cs` also adds code-level filters with the same effect for EF `Database.Command` / `Model.Validation` (Warning), TodoService (None) and TodoPurgeWorker (Warning), plus a console provider. |
| `AllowedHosts` | framework | `*` | Host filtering (not restricted). |
| `DetailedErrors` | framework | Dev/Prod `false` | Host setting. |
| `ASPNETCORE_URLS`, `ASPNETCORE_HTTPS_PORT`, `Kestrel:Endpoints:Https:Url` | none | absent | Development only: `ResolveDevelopmentLoopbackOrigins` in `Program.cs` adds these HTTPS origins, together with `https://localhost:7183`, to the CSP. |

### Settings fixed in code (not configurable)

- **Data Protection**: application name `ProjectManagement_SDD`. Key directory from env `DP_KEYS_DIR`. See [Environment variables](#environment-variables).
- **Identity**: minimum password length 8, lowercase required, no digit/uppercase/symbol requirement, lockout after 5 failures, unconfirmed accounts allowed, unique email not required.
- **Cookies**: auth cookie `PMAuth` in Development and `__Host-PMAuth` elsewhere; antiforgery cookie `PMAntiforgery` / `__Host-PMAntiforgery`; antiforgery header `X-CSRF-TOKEN` (the legacy header `RequestVerificationToken` is copied across). Both cookies use SameSite=Strict. They are `Secure` outside Development. The auth cookie expires after 60 minutes, sliding.
- **Rate limiters**: `login` allows 10 requests per 5 minutes and `docUpload` allows 10 per minute. Both are fixed-window and not partitioned per client.
- **HSTS**: 365 days, preload, includeSubDomains. Enabled outside Development.
- **Forwarded headers**: `X-Forwarded-For` and `X-Forwarded-Proto`, with `KnownNetworks` and `KnownProxies` cleared, so the headers are accepted from any source. `app.UseForwardedHeaders()` is always called. `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is not needed.
- **SignalR**: KeepAlive 15 s, ClientTimeout 90 s, Handshake 15 s. Hub at `/hubs/notifications`.
- **Npgsql switches**: `Npgsql.EnableLegacyTimestampBehavior=true`, `Npgsql.DisableDateTimeInfinityConversions=true`.
- **Time zone**: IST (`Asia/Kolkata`), resolved through `TimeZoneConverter` in `Utilities/TimeZoneHelper.GetIst()` and exposed by `Infrastructure/IstClock`. There is no configuration key for it.

### Request body size (derived)

`Infrastructure/UploadRequestLimitResolver.Resolve` sets Kestrel `MaxRequestBodySize`, `IISServerOptions.MaxRequestBodySize` and `FormOptions.MultipartBodyLengthLimit`. The value is **the largest of** `ProjectDocuments:MaxSizeMb`×1 MiB, `ProjectVideos:MaxFileSizeBytes`, `ProjectPhotos:MaxFileSizeBytes`, `ProjectOfficeReports:VisitPhotos:MaxBatchSizeBytes`, `ProjectOfficeReports:VisitPhotos:MaxFileSizeBytes`, `ProjectOfficeReports:SocialMediaPhotos:MaxFileSizeBytes`, `IprAttachments:MaxFileSizeBytes`, `FfcAttachments:MaxFileSizeBytes`, `DocRepo:MaxFileSizeBytes` and `MediaLibrary:Processing:MaxImageFileSizeBytes`, **plus 8 MiB**, with a floor of 32 MiB. Only keys actually present in configuration count. With the shipped files this is 200 MiB + 8 MiB = 208 MiB, which fits under the 256 MiB `web.config` ceiling. `ArppAttachments:MaxFileSizeBytes` is **not** part of this calculation.

---

## Storage and file roots

The code has **no single data root**. Each module resolves its own directory:

| Area | Resolution (code) |
| --- | --- |
| Upload root (`IUploadRootProvider.RootPath`) | Env `PM_UPLOAD_ROOT` → `ProjectPhotos:StorageRoot`. When `StorageRoot` is empty, `ProjectPhotoOptionsSetup` fills in `{WebRootPath or ContentRootPath}/uploads`, which is normally **`wwwroot/uploads`**. The code fallback `/var/pm/uploads` in `UploadRootProvider` is only reached if `StorageRoot` is still empty. Environment variables and `~` are expanded. If the directory cannot be created, the provider falls back to `%LOCALAPPDATA%/ProjectManagement/uploads` (Windows) or `{ContentRoot}/uploads`. Dev/Prod set `D:`/`F:/ProjectManagementData/uploads`. |
| Project files | `{uploadRoot}/{ProjectDocuments:ProjectsSubpath}/{projectId}/` with the `PhotosSubpath`, `StorageSubPath`, `CommentsSubpath` and `VideosSubpath` subfolders. |
| Visit / social-media photos | `{uploadRoot}/{StoragePrefix}` (social media requires the `{eventId}` token). |
| IPR attachments | `IprAttachments:StorageRoot` (relative paths are resolved against the upload root) or `{uploadRoot}/{StorageFolderName}`. |
| FFC attachments | `FfcAttachments:StorageRoot` (relative paths are resolved against the upload root) or `{uploadRoot}/{StorageFolderName}`. |
| ARPP attachments | `ArppAttachments:StorageFolderName` under the upload root. |
| Project videos | `ProjectVideos:StorageRootOverride/{projectId}` if set, otherwise the project `VideosSubpath`. |
| Document Repository | `DocRepo:RootPath`. Relative paths are resolved against **ContentRoot**. Files are stored as `{yyyy}/{MM}/{guid}.pdf`. |
| DocRepo OCR work | `DocRepo:OcrWorkRoot`. Relative paths are resolved against the **upload root**; an empty value means `{uploadRoot}/ocr-work`. |
| Project-document OCR work | `ProjectDocuments:Ocr:WorkRoot`. Relative paths are resolved against **`AppContext.BaseDirectory`**. |
| Media cache / face models | `MediaLibrary:CacheRoot`, `MediaLibrary:People:ModelRoot`. Relative paths are resolved against ContentRoot. |
| Startup diagnostics | `{Storage:DataRoot}/startup-diagnostics` if `Storage:DataRoot` is fully qualified, otherwise `{ContentRoot}/startup-diagnostics` (`Infrastructure/StartupFailureReporter`). |
| Compendium diagnostics | Env `PRISM_COMPENDIUM_DIAGNOSTICS_DIR`, otherwise `{AppContext.BaseDirectory}/logs/compendium`. |
| Data Protection keys | Env `DP_KEYS_DIR`, otherwise Development: `%LOCALAPPDATA%/PRISM-ERP/DataProtectionKeys`, other environments: `/var/pm/keys`. On Windows `/var/pm/keys` means `\var\pm\keys` at the root of the current drive, which is **outside** the data folder. |

| Key | Code default | appsettings | Notes |
| --- | --- | --- | --- |
| `Storage:DataRoot` | none | Dev `D:/ProjectManagementData`, Prod `F:/ProjectManagementData`, base absent | **Used only by `StartupFailureReporter`.** It does *not* relocate uploads, DocRepo, OCR or media folders. Each of those needs its own key, as in the Dev/Prod files. |

---

## Project photos, documents and videos

### `ProjectPhotos` → `Services/Projects/ProjectPhotoOptions`

| Key | Code default | appsettings | Notes |
| --- | --- | --- | --- |
| `StorageRoot` | `""`, then set to `wwwroot/uploads` by `ProjectPhotoOptionsSetup` | base absent; Dev/Prod `…/ProjectManagementData/uploads` | Shared upload root (see above). |
| `MaxFileSizeBytes` | 5 MiB | base 10 MiB; Dev/Prod 20 MiB | |
| `MinWidth` / `MinHeight` | 720 / 540 | same | |
| `MaxWidth` / `MaxHeight` / `MaxPixelCount` | 12000 / 12000 / 60,000,000 | absent | Decompression-bomb guard. |
| `AllowedContentTypes` | jpeg, png, webp | same | |
| `Derivatives` | `xl` 1600×1200 q90, `md` 1200×900 q85, `sm` 800×600 q80, `xs` 400×300 q75 | same | Keep the keys stable. `CompendiumPdf:CoverPhotoDerivativeKey` must name one of them. |
| `MaxProcessingConcurrency` / `MaxEncodingConcurrency` | `max(CPU/2, 1)` | 2 / 2 | |

### `ProjectDocuments` → `Configuration/ProjectDocumentOptions` (validated by `ProjectDocumentOptionsValidator`)

| Key | Code default | appsettings | Notes |
| --- | --- | --- | --- |
| `ProjectsSubpath` | `projects` | same | Must be relative, non-empty, with no `.`/`..`. |
| `PhotosSubpath` | `""` | same | May be empty. |
| `StorageSubPath` (alias `DocumentsSubpath`) | `docs` | same | |
| `CommentsSubpath` | `comments` | same | |
| `VideosSubpath` | `videos` | absent | |
| `TempSubPath` | `temp` | same | |
| `MaxSizeMb` | 100 | 100 | Must be ≥ 0. |
| `AllowedMimeTypes` | pdf, doc, ppt, xls, docx, pptx, xlsx | same | Must be non-empty. |
| `EnableVirusScan` | `false` | `false` | `DocumentService` throws on upload when this is `true`, because **no `IVirusScanner` implementation is registered**. |

### `ProjectDocuments:Ocr` → `Configuration/ProjectDocumentOcrOptions` (validated: data annotations, `ProjectDocumentOcrOptionsValidator`)

| Key | Code default | appsettings | Notes |
| --- | --- | --- | --- |
| `WorkRoot` | `""` (**required**) | base `App_Data/project-ocr`; Dev/Prod `…/project-ocr` | |
| `InputSubpath` / `OutputSubpath` / `LogsSubpath` | `input` / `output` / `logs` | same | Relative, no traversal. |
| `OcrExecutablePath` | null, meaning `ocrmypdf` on PATH | base absent; **Dev/Prod `C:/Python311/Scripts/ocrmypdf.exe`** | If set, the file **must exist** or startup fails. |
| `EnableWorker` | `true` | `true` | Read directly in `Program.cs` to register `Hosted/ProjectDocumentOcrWorker`. |

### `ProjectDocuments:TextExtraction` → `Configuration/ProjectDocumentTextExtractorOptions` (validated)

Not present in any appsettings file.

| Key | Code default | Notes |
| --- | --- | --- |
| `DerivativeStoragePrefix` | `ocr` | Required, relative. |
| `EnablePdfConversion` | `false` | LibreOffice conversion. |
| `LibreOfficeExecutablePath` | null | |

### `ProjectVideos` → `Services/Projects/ProjectVideoOptions`

| Key | Code default | appsettings |
| --- | --- | --- |
| `MaxFileSizeBytes` | 200 MiB | 209715200 |
| `AllowedContentTypes` | mp4, webm, ogg | same |
| `PosterFileExtension` | `.jpg` | same |
| `StorageRootOverride` | null | absent |
| `MaxDuration` | null | absent. **Not read anywhere.** |

### `Projects:Retention` → `Configuration/ProjectRetentionOptions`

| Key | Code default | appsettings | Used by |
| --- | --- | --- | --- |
| `TrashRetentionDays` | 30 | 30 (base only) | `ProjectRetentionWorker`, admin recovery pages |
| `RemoveAssetsOnPurge` | `true` | `true` | `ProjectRetentionWorker` |

---

## Document Repository (`DocRepo`) → `Services/DocRepo/DocRepoOptions` (validated)

| Key | Code default | appsettings | Notes |
| --- | --- | --- | --- |
| `RootPath` | `App_Data/DocRepo` (`[Required]`) | base `App_Data/DocRepo`; Dev/Prod `…/DocRepo` | Relative paths are resolved against ContentRoot. |
| `MaxFileSizeBytes` | 104,857,600 | Dev/Prod only | |
| `EnableIngestion` | `true` | `true` | `DocRepoIngestionService`. |
| `IngestionOfficeCategoryId` / `IngestionDocumentCategoryId` | null | 1 / 1 | |
| `IngestionUserId` | `system` | same | |
| `EnableOcrWorker` | `true` | Dev/Prod `true`, base absent | Read in `Program.cs` to register `Hosted/DocRepoOcrWorker`. If `true`, `OcrWorkRoot` must be set. |
| `OcrExecutablePath` | null, meaning `ocrmypdf` | Dev/Prod `C:/Python311/Scripts/ocrmypdf.exe` | Not checked for existence. |
| `OcrWorkRoot` | null | `ocr-work` | Relative to the upload root. |
| `OcrInput` / `OcrOutput` / `OcrLogs` | `input` / `output` / `logs` | same | |
| `EnableListViewUxUpgrade` | n/a | `true` | Read directly by `Areas/DocumentRepository/Pages/Documents/Index.cshtml.cs`. |
| `EnableAutoApplyFilters` | n/a | `false` | **Never read by code.** |

### `OcrBackfill` → `Configuration/OcrBackfillOptions` (validated, no rules)

| Key | Code default | appsettings | Notes |
| --- | --- | --- | --- |
| `Enabled` | `false` | absent | When `true`, registers `Hosted/OcrTextBackfillWorker`. |

---

## Search (`Search:V2`) → `Services/SearchV2/SearchV2Options` (validated: data annotations, `MaxPageSize >= PageSize`)

Registered by `SearchV2ServiceCollectionExtensions.AddSearchV2`. The hosted services are `SearchIndexWorker` and `SearchTelemetryRetentionWorker`.

| Key | Code default | appsettings | Range |
| --- | --- | --- | --- |
| `Enabled` | `true` | `true` | |
| `ServeV2` | **`false`** | **`true`** | |
| `ShadowMode` | `true` | `true` | |
| `ServeV2Users` / `ServeV2Roles` | `[]` | absent | |
| `PageSize` / `MaxPageSize` | 20 / 50 | same | 5–100 |
| `SuggestionLimit` | 6 | 6 | 5–20 |
| `FuzzyThreshold` | 0.28 | 0.28 | 0.05–0.95 |
| `ReciprocalRankK` | 60 | 60 | 1–500 |
| `CanonicalEntityBoost` | 0.0025 | absent | 0–0.05 |
| `FuzzyFallbackStrongCandidateThreshold` | 1 | absent | 1–20 |
| `CorrectionMinTokenLength` / `CorrectionMaxTokens` / `CorrectionMaxEditDistance` / `CorrectionMaxLengthDelta` | 4 / 6 / 3 / 3 | absent | see class |
| `CorrectionCandidateTrigramThreshold` / `CorrectionMinConfidence` / `CorrectionCandidateLimit` | 0.18 / 0.62 / 32 | absent | see class |
| `IndexVersion` / `ProjectionVersion` | 2 / 4 | 2 / 4 | Changing `ProjectionVersion` triggers a rebuild. |
| `WorkerIntervalSeconds` | 15 | 15 | 5–3600 |
| `WorkItemLeaseMinutes` | 10 | 10 | 1–1440 |
| `FullReconciliationMinutes` | 1440 | 1440 | 1–10080 |
| `QueryLogRetentionDays` | 90 | 90 | 1–3650 |
| `MaxSnippetCharacters` | 420 | 420 | 120–1200 |

---

## Notifications, email, users, tasks

### `Notifications:Retention` → `Services/Notifications/NotificationRetentionOptions` (`NotificationRetentionService`)

| Key | Code default | base | Dev/Prod |
| --- | --- | --- | --- |
| `SweepInterval` | 1 h (non-positive values also mean 1 h) | `00:15:00` | `00:05:00` |
| `MaxAge` | null (off) | `30.00:00:00` | `14.00:00:00` |
| `MaxPerUser` | null (off) | 200 | 100 |
| `CompletedDispatchMaxAge` | 14 days | 14 days | 14 days |
| `DeadLetterMaxAge` | 90 days | 90 days | 90 days |

### `Email` (read by `Program.cs` and `Services/SmtpEmailSender`, no Options class)

| Key | appsettings | Notes |
| --- | --- | --- |
| `Email:Smtp:Host` | `""` | Empty registers `NoOpEmailSender`. Non-empty registers `SmtpEmailSender`. |
| `Email:Smtp:Port` | 587 | If the value is not parseable, the sender uses **25**. SSL is always on. |
| `Email:Smtp:Username` / `Password` | `""` | |
| `Email:From` | `""` | Empty uses `Username`, then `no-reply@example.com`. |

**No application code consumes `IEmailSender`.** Enabling SMTP therefore sends no mail today.

### `UserLifecycle` → `Services/UserLifecycleOptions`

| Key | Code default | appsettings | Used by |
| --- | --- | --- | --- |
| `HardDeleteWindowHours` | 72 | 72 (base) | `UserLifecycleService`, `UserPurgeWorker`, `AdminUserQueryService` |
| `UndoWindowMinutes` | 15 | 15 | same |

### `Todo` → `Models/TodoOptions`

| Key | Code default | appsettings | Used by |
| --- | --- | --- | --- |
| `RetentionDays` | 7 | 7 | `TodoPurgeWorker` |
| `MaxOpenTasks` | 500 | 500 | `TodoService` |

### `Conference` → `Configuration/ConferenceOptions` (validated)

| Key | Code default | appsettings | Rule / used by |
| --- | --- | --- | --- |
| `CompletedProjectRetentionDays` | 90 | 90 | 1–730. Used by `ConferenceProjectScopeService`. |

### `Notebook:Trash` → `Configuration/NotebookTrashOptions` (`Hosted/NotebookTrashRetentionWorker`)

| Key | Code default | appsettings |
| --- | --- | --- |
| `RetentionDays` | 30 | 30 (base) |
| `SweepInterval` | 6 h | `06:00:00` |

---

## Administration, audit, usage

### `AdminLoginMonitoring` → `Configuration/AdminLoginMonitoringOptions` (validated)

| Key | Code default | appsettings | Rule |
| --- | --- | --- | --- |
| `WorkdayStart` / `WorkdayEnd` | 08:00 / 18:00 | same | Start < End ≤ 24:00 |
| `MarkWeekendsForReview` | `true` | `true` | |
| `DefaultLookbackDays` / `MaximumLookbackDays` | 30 / 365 | same | Maximum 1–3660, default ≤ maximum |
| `MaximumChartPoints` | 5000 | 5000 | 250–100000 |
| `DefaultReviewPageSize` / `MaximumReviewPageSize` | 25 / 100 | same | 10–500 |
| `DuplicateWindowMinutes` | 2 | absent | 1–30 |

### `AdminRecovery` → `Configuration/AdminRecoveryOptions` (validated, absent from appsettings)

| Key | Code default | Rule |
| --- | --- | --- |
| `DueSoonDays` | 7 | 1–90 |
| `DefaultPageSize` / `MaximumPageSize` | 25 / 100 | 10–500 |
| `MaximumBulkDocuments` | 100 | 1–500 |
| `RecentOperationCount` | 8 | 1–50 |
| `LegacyImportPreviewRows` | 100 | 10–1000 |
| `LegacyImportStagingMinutes` | 30 | 5–240 |

### `Audit:Retention` → `Configuration/AuditRetentionOptions` (validated, absent from appsettings; `Hosted/AuditRetentionWorker`)

| Key | Code default | Rule |
| --- | --- | --- |
| `Enabled` | `false` | |
| `RetentionDays` | 365 | ≥ 30 when enabled |
| `SweepInterval` | 1 day | Clamped to ≥ 15 min. |
| `BatchSize` | 5000 | 100–25000 |

### `ErpUsage` → `Configuration/ErpUsageOptions` (validated)

Used by `UserActivityRecorder` (called from `Infrastructure/Usage/ErpUsageMiddleware`), `UserActivityRetentionWorker`, `Pages/Usage/Index` and the usage query services.

| Key | Code default | appsettings | Rule |
| --- | --- | --- | --- |
| `TrackingInceptionUtc` | `2026-07-14T07:30:00Z` | same | Must be UTC. |
| `BucketMinutes` | 5 | absent | 1–30 |
| `HeartbeatIntervalSeconds` | 180 | absent | 60–900, and shorter than the idle window |
| `InteractiveIdleMinutes` | 10 | absent | 2–60 |
| `RetentionDays` | 400 | absent | 30–1095, ≥ `MaximumLookbackDays` |
| `RegularUserThresholdPercent` | 80 | absent | 1–100 |
| `MaximumLookbackDays` | 365 | absent | 90–365 |
| `MaximumExportRows` | 2000 | absent | 100–20000 |
| `WorkingDays` | Mon–Sat | absent | Non-empty, no duplicates, no Sunday. |

### `FileDownload` → `Configuration/FileDownloadOptions`

| Key | Code default | appsettings | Used by |
| --- | --- | --- | --- |
| `TokenLifetimeMinutes` | 30 | 30 | `FileAccessTokenService`. Tokens are Data Protection–protected (purpose `FileAccessTokenService`) and served by `Controllers/FilesController` (`/files`). |
| `BindTokensToUser` | `true` | `true` | `ProtectedFileUrlBuilder` |

---

## Attachments (IPR, FFC, ARPP)

| Key | Code default | appsettings | Class |
| --- | --- | --- | --- |
| `IprAttachments:MaxFileSizeBytes` | 20 MiB | 20971520 | `Configuration/IprAttachmentOptions` → `IprAttachmentStorage`, `IprWriteService` |
| `IprAttachments:AllowedContentTypes` | `application/pdf` | same | |
| `IprAttachments:AllowedExtensions` | `.pdf` | absent | |
| `IprAttachments:StorageFolderName` | `ipr-attachments` | same | |
| `IprAttachments:StorageRoot` | null | absent | |
| `FfcAttachments:MaxFileSizeBytes` | 20 MiB | 20971520 | `Configuration/FfcAttachmentOptions` → `FfcAttachmentStorage` |
| `FfcAttachments:StorageFolderName` | `ffc` | absent | |
| `FfcAttachments:StorageRoot` | null | absent | |
| `ArppAttachments:MaxFileSizeBytes` | 100 MiB | absent | `Configuration/ArppAttachmentOptions` → `FileSystemArppAttachmentStorage`, `ArppAttachmentService` |
| `ArppAttachments:StorageFolderName` | `arpp` | absent | |
| `ArppAttachments:IngestIntoDocumentRepository` | `true` | absent | |

---

## Project Office Reports

### `ProjectOfficeReports:VisitPhotos` → `Areas/ProjectOfficeReports/Application/VisitPhotoOptions`

| Key | Code default | appsettings |
| --- | --- | --- |
| `MaxFileSizeBytes` | 20 MiB | 20971520 |
| `MaxFilesPerUpload` | 20 | 20 |
| `MaxBatchSizeBytes` | 100 MiB | 104857600 |
| `MaxPhotosPerVisit` | 50 | 50 |
| `MaxWidthPixels` / `MaxHeightPixels` / `MaxMegapixels` | 10000 / 10000 / 60 | absent |
| `MinWidth` / `MinHeight` | 720 / 540 | same |
| `AllowedContentTypes` | jpeg, png, webp | same |
| `Derivatives` | `xl` 1600×1200 q90, `md` 1200×900, `sm` 800×600, `xs` 400×300 | **`xl` 1920×1080 q85, `lg` 1280×720 q85, `md` 800×600 q80, `sm` 640×480 q80, `xs` 400×300 q75** |
| `StoragePrefix` | `project-office-reports/visits` | same |

### `ProjectOfficeReports:SocialMediaPhotos` → `SocialMediaPhotoOptions`

| Key | Code default | appsettings |
| --- | --- | --- |
| `MaxFileSizeBytes` | 10 MiB | 20 MiB |
| `MaxFilesPerUpload` | 20 | 20 |
| `MinWidth` / `MinHeight` | null (values ≤ 0 also mean null) | absent |
| `AllowedContentTypes` | jpeg, png, webp | same |
| `Derivatives` | `story` 1080×1920, `feed` 1200×1200, `thumb` 600×600 | same |
| `StoragePrefix` | `org/social/{eventId}` | same. Must contain `{eventId}` (`UploadRootProvider.GetSocialMediaRoot` throws otherwise). |

### `ProjectOfficeReports:TrainingTracker` → `Configuration/TrainingTrackerOptions`

| Key | Code default | appsettings | Notes |
| --- | --- | --- | --- |
| `Enabled` | **`false`** | `true` | The Training pages show "disabled" when this is false. |
| `MaxExportTrainingRows` | 5000 | 5000 | |
| `MaxExportRosterRows` | 50000 | 50000 | |
| `ExportTimeoutSeconds` | 120 | 120 | |

### `Proliferation:Export` → `Configuration/ProliferationExportOptions` (absent from appsettings)

| Key | Code default |
| --- | --- |
| `MaximumProjectRows` | 5000 |
| `MaximumUnitRows` | 50000 |
| `TimeoutSeconds` | 120 |

---

## Publications / Compendium PDF (`CompendiumPdf`) → `Configuration/CompendiumPdfOptions`

Used by `CompendiumReadService` and `CompendiumExportService`.

| Key | Code default | appsettings (base) |
| --- | --- | --- |
| `Title` | `Simulators Compendium` | same |
| `Subtitle` | `Available for Proliferation` | absent |
| `UnitDisplayName` | `Simulator Development Division` | **`""`** (overrides the default with an empty string) |
| `IssuerDisplayName` | `Simulator Development Division` | absent |
| `FileNamePrefix` | `SDD_Simulators_Compendium` | absent |
| `MiscCategoryNames` | `Misc`, `Miscellaneous` | same |
| `CoverPhotoDerivativeKey` | `md` | `md` |
| `PreferredPhotoFormat` | `jpg` | absent |
| `PreferWebp` | `false` | **`true`** |
| `ShowMissingPhotoPlaceholder` | `true` | absent |

Publication fonts are found through env `PRISM_PUBLICATION_FONTS_DIR`, then ContentRoot/WebRoot `fonts/publications` (`Utilities/Reporting/PublicationFontContract`).

---

## Media Library / Photos (`MediaLibrary`) → `Features/MediaLibrary/Options/MediaLibraryOptions` (validated by `MediaLibraryOptionsValidator`)

Top level:

| Key | Code default | appsettings | Notes |
| --- | --- | --- | --- |
| `Enabled` | `true` | `true` | Also read directly by `Program.cs`. When `false`, the media migration/readiness gate is skipped. A media migration failure does not stop the core app: Photos stays unavailable and a diagnostic is written. |
| `AutoMigrate` | `false` | `false` | **Bound but ignored** (legacy). |
| `CacheRoot` | `App_Data/media-cache` | Dev/Prod `…/media-cache` | Required while external sources or processing are enabled. |
| `ScannerWorkerEnabled`, `ProcessingWorkerEnabled`, `Sources` | null / null / [] | absent | Legacy prototype bind points. |

`Catalogue`: `Enabled` (true), `SynchronizePrismMedia` (true), `SynchronizeIntervalSeconds` (0, meaning use minutes; otherwise 5–3600), `SynchronizeIntervalMinutes` (code default **10**, appsettings **5**; 1–10080).

`ExternalSources`: `Enabled` (false), `ScannerWorkerEnabled` (false), `DefaultScanIntervalMinutes` (30), `ScanBatchSize` (250, 10–5000), `IdleDelaySeconds` (20), `ScanLeaseMinutes` (15, 2–240), `Sources[]`. Each source has `Key`, `Name`, `Type` (`FileSystem` only), `RootPath` (fully qualified local/UNC path), `Enabled`, `VisibleInLibrary` (true), `ReadOnly` (true), `IncludeSubfolders` (true), `ScanIntervalMinutes`, and `AllowedExtensions` (defaults `.jpg .jpeg .png .webp .gif .bmp .mp4 .webm .mov .m4v .ogg`). Sample configuration: `ops/media-library/*.example.json`.

`Processing`: `WorkerEnabled` (true), `IdleDelaySeconds` (20), `BatchSize` (1, 1–16), `MaxAttempts` (5, 1–20), `MaxImageFileSizeBytes` (200 MiB, 1 MiB–2 GiB), `ThumbnailMaxPixels` (480, 128–2048), `PreviewMaxPixels` (1920, up to 8192), `WebpQuality` (84, 40–100).

`BulkDownload` (absent from appsettings): `MaxItems` (120, 1–500), `MaxSourceBytes` (2 GiB, 10 MB–50 GB).

`Classification`: all thresholds in the class. The appsettings values equal the code defaults. Probabilities must be within 0–1, `NaturalPhotoAutoAcceptThreshold ≥ PhotographThreshold`, and `PhotographMinimumScoreMargin ≥ MinimumScoreMargin`.

`People` (face intelligence):
- `Enabled` and `WorkerEnabled` default to `false` in code and are `false` in base appsettings, but **Dev and Prod set both to `true`**.
- `AutoConfirmEnabled` must be `false`; validation fails otherwise.
- `ModelRoot` defaults to `App_Data/media-models`.
- `Detector` and `Embedder` (`FaceModelOptions`) each require `Key`, `Version`, `Adapter`, `FileName`, a 64-hex `Sha256`, `License`, and either a valid http(s) `SourceUrl` or complete offline provenance (`Publisher`, `ApprovedArtifactId`, `AcquiredOn` in `yyyy-MM-dd`).
- Keys present in code but absent from appsettings: `CandidateSearchTimeoutSeconds` (60), `CandidateProcessingStaleSeconds` (180), `CandidateFailureRetryDelaySeconds` (300), `ReviewTriageBatchLimit` (100), `GroupingReviewModerateSimilarityThreshold` (0.50), `GroupingReviewStrongSimilarityThreshold` (0.65), and the model tensor fields (`BoxesOutputName`, `ScoresOutputName`, `LandmarksOutputName`, `BoxesAreNormalized`, `MeanR/G/B`, `StdR/G/B`).

Hosted services are registered from the options as configured at startup (`MediaLibraryServiceCollectionExtensions.AddMediaLibrary`):

| Hosted service | Registered when |
| --- | --- |
| `PrismMediaOutboxWorker` | Catalogue enabled and `SynchronizePrismMedia` |
| `MediaSourceScannerWorker` | Catalogue enabled and (`SynchronizePrismMedia` or the scanner worker is enabled) |
| `MediaProcessingWorker`, `MediaAvailabilityReconciliationWorker` | Any processing worker is enabled |
| `FaceAnalysisQueueWorker` | People worker enabled |
| `FaceCandidateRefreshWorker` | People worker enabled and `CandidateSearchEnabled` |
| `FaceIdentityGroupingRefreshWorker` | People worker enabled and `GroupingEnabled` |

---

## Security headers

| Key | Default | Notes |
| --- | --- | --- |
| `Security:Csp:ConnectSrcExtra` | `[]` | Appended to `connect-src 'self'`. |
| `SecurityHeaders:ContentSecurityPolicy:ConnectSources` | `[]` | Legacy alias, merged into `connect-src`. |
| `Security:Csp:ImgSrcExtra` | `[]` | Appended to `img-src 'self' data: blob:`. |
| `Security:Csp:StyleSrcExtra` | `[]` | Appended to `style-src`/`style-src-elem 'self'`. `'unsafe-inline'` is added only in Development and on `/Calendar` and `/Projects/Meta/Edit`. |
| `Security:Csp:FontSrcExtra` | `[]` | Appended to `font-src 'self' data:`. |

Fixed headers: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, COOP/CORP `same-origin`, `frame-ancestors 'none'`, `script-src 'self'`. Document viewer, preview and `/files` responses instead get a relaxed `frame-ancestors 'self'` policy with COOP `unsafe-none` and CORP `cross-origin`.

---

## Environment variables

| Variable | Read by | Effect |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | host, design-time factories | Selects `appsettings.{Env}.json`, cookie names, HSTS and the Data Protection default. |
| `ConnectionStrings__DefaultConnection` | standard provider; also an explicit fallback in `Program.cs` | Connection string. |
| `DefaultConnection` | `Program.cs` | Fallback, used only if the configured connection string is empty. |
| `DP_KEYS_DIR` | `Program.cs`, `AdminSystemHealthService` | Data Protection key folder. The health page warns in Production when it is not set. |
| `PM_UPLOAD_ROOT` | `UploadRootProvider` | Overrides `ProjectPhotos:StorageRoot` for every upload consumer. |
| `PRISM_BOOTSTRAP_ADMIN_PASSWORD` | `IdentitySeeder` | One-time admin password (when seeders run). |
| `PRISM_COMPENDIUM_DIAGNOSTICS_DIR` | `CompendiumGenerationDiagnostics`, Compendium page | Diagnostic log folder. |
| `PRISM_PUBLICATION_FONTS_DIR` | `PublicationFontContract`, `PublicationFontRegistry` | External DM Sans font root. |
| `PM_DATA_ROOT`, `PM_FILE_BACKUP_ROOT`, `PM_FILE_BACKUP_SOURCE`, `PM_BACKUP_DIR`, `PM_DUMP_FILE`, `PGRESTORE_USER`, `PGBIN`, `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER` | `ops/*.sh` only | Backup and restore scripts. **The application does not read `PM_DATA_ROOT`.** Linux script defaults: data `/srv/projectmanagement-data`, DB user `pm_backup`, restore user `postgres`. The Windows `ops/backup-files.ps1` and `restore-files.ps1` take `-DataRoot` (default `D:\ProjectManagementData`, which does not match Production's `F:`). |

## Command-line switches

| Switch | Effect |
| --- | --- |
| `--backfill-forecast` | Builds the host, runs the mandatory migration gate (and seeders if enabled), then runs `ForecastBackfillService.BackfillAsync()`, logs the number of updated stages and exits. |
| `--compendium-offline-self-test` | Runs `CompendiumOfflineSelfTest.Run()` **before** the host or configuration is built (no database, no HTTP). It checks the DM Sans fonts, SkiaSharp, QuestPDF and PdfPig, sets the process exit code, and exits. |

## Keys in appsettings that code does not use

- `DocRepo:EnableAutoApplyFilters`: never read.
- `Database:ApplyMigrationsOnStartup`: ignored (migrations are mandatory). Only logged and displayed.
- `MediaLibrary:AutoMigrate`: bound, ignored.
- `Storage:DataRoot`: read only for startup-failure diagnostics.
- `ProjectVideos:MaxDuration` (Options property, not in appsettings): never read.
