# Architecture and platform wiring

This guide describes how PRISM is assembled: the solution layout, the startup sequence, the HTTP pipeline, identity and authorization, background workers, HTTP APIs, SignalR and the database startup gate. `Program.cs` (top-level statements) is the composition root and the authority for everything below; symbol names are given so you can search for them rather than relying on line numbers.

Related guides: [data-domain.md](data-domain.md), [infrastructure-services.md](infrastructure-services.md), [configuration-reference.md](configuration-reference.md), [../MIGRATIONS-POLICY.md](../MIGRATIONS-POLICY.md).

## Technology

- ASP.NET Core 8 (`net8.0`) Razor Pages, plus a small number of MVC API controllers and minimal APIs.
- EF Core 8 on PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.x). Two contexts: `ApplicationDbContext` (`Data/`) and `MediaLibraryDbContext` (`Features/MediaLibrary/Data/`, history table `__EFMigrationsHistory_MediaLibrary`).
- ASP.NET Core Identity (`ApplicationUser`, `IdentityRole`), cookie authentication.
- SignalR for notifications.
- Document and report generation: QuestPDF, ClosedXML, DocumentFormat.OpenXml, PdfPig, SkiaSharp, ImageSharp; OCR through the external `ocrmypdf` process; ONNX Runtime for media face analysis.
- Front-end assets under `wwwroot/`; Node is used for the notebook bundle, JS tests and the view linter (`package.json` scripts).

## Solution structure

```
Program.cs / ProgramTypes.cs  Composition root (DI, auth, pipeline, minimal APIs, startup gate)
Application/                  Vertical-slice services: Ffc/ and Ipr/ attachment storage and read/write
                              services; Security/ (FileSecurityValidator: relative-path and virus-scan checks)
Areas/
  Admin/                      Administration workspace (users, access governance, logs, recovery,
                              master data, maintenance, diagnostics, calendar, lookups, help)
  Common/                     Shared pages (global Search)
  Dashboard/                  Dashboard view components
  DocumentRepository/         Document repository (upload, reader, admin, OCR-backed search)
  Identity/                   Login, Logout, AccessDenied, Manage/Index, Manage/ChangePassword (scaffolded; the
                              default Identity UI is not registered)
  ProjectOfficeReports/       FFC, ARPP, IPR, Proliferation, ToT, Training, Visits, Social media,
                              Progress review; includes Api/ controllers and Application/ policies
Configuration/                Options classes, validators, RoleNames, Policies, AdminPolicies
Contracts/                    DTOs shared by APIs, services and the SignalR hub
Controllers/                  API controllers (files, notebook, ARPP/industry-partner lookups)
Data/                         ApplicationDbContext, design-time factory, IdentitySeeder, StageFlowSeeder
Features/                     Feature slices: MediaLibrary (own DbContext, migrations, workers),
                              Notifications, Remarks, Analytics and Users (minimal API maps), Backfill
Helpers/                      Stateless helpers
Hosted/                       Background workers (OCR, backfill, audit and notebook retention)
Hubs/                         NotificationsHub
Infrastructure/               Startup migrator, schema validator, lineage manifest, password filter,
                              IST clock, transaction scope, CSV safety, upload limit resolver, usage middleware
Migrations/                   ApplicationDbContext migrations + immutable-migration-ids.txt
Models/                       Entities and value types
Pages/                        End-user Razor Pages (Projects, Dashboard, Calendar, Notebook, Photos,
                              ProjectIdeas, Workspace, Tasks, Process, Usage, Settings, ...)
Resources/                    Embedded presentation resources (FFC, project briefing)
Services/                     Business services, notification stack, workers, Search V2, storage
Utilities/                    Low-level helpers (reporting/PDF, CSV, sanitisation)
ViewComponents/, ViewModels/, Views/Shared/   MVC view infrastructure used by Razor Pages
wwwroot/                      Static assets and JS modules
App_Data/                     Runtime data (doc repo, FFC, media cache, face models)
ProjectManagement.Tests/      xUnit tests
tools/, ops/                  Migration helper, font/publication checks, backup/restore and publish scripts
```

## Startup sequence

`Program.cs` runs these phases in order:

1. **Offline self-test switch.** `--compendium-offline-self-test` (`CompendiumOfflineSelfTest.CommandLineSwitch`) runs before the host is built and exits.
2. **Logging.** Console logging; EF command and model-validation logs are raised to Warning; `TodoService` logging is suppressed.
3. **Data protection.** Keys persist to `DP_KEYS_DIR`. If that is unset, Development uses `%LOCALAPPDATA%/PRISM-ERP/DataProtectionKeys` and other environments use `/var/pm/keys`. The application name is `ProjectManagement_SDD`.
4. **Database.** The connection string comes from `ConnectionStrings:DefaultConnection`, falling back to the `ConnectionStrings__DefaultConnection` or `DefaultConnection` environment variables. `ApplicationDbContext` gets the `PrismMediaOutboxSaveChangesInterceptor`. `AddMediaLibrary` registers `MediaLibraryDbContext` against the same connection string. Npgsql legacy timestamp behaviour is enabled.
5. **Identity.** `AddIdentity<ApplicationUser, IdentityRole>`: no confirmed account, no unique email, minimum 8 characters with a lowercase letter, lockout after 5 failures.
6. **Authorization policies.** Listed in full [below](#authorization-policies).
7. **Antiforgery.** Header `X-CSRF-TOKEN`. The cookie is `__Host-PMAntiforgery` (`PMAntiforgery` in Development), HttpOnly, SameSite=Strict, Secure except in Development.
8. **Application cookie.** `__Host-PMAuth` (`PMAuth` in Development), HttpOnly, SameSite=Strict, 60-minute sliding expiry, login at `/Identity/Account/Login`. Requests under `/api` or `/hubs` get 401/403 instead of a redirect (`IsApiOrRealtimeRequest`).
9. **Rate limiter.** Two fixed-window limiters, each **global rather than per client**:
   - `login`: 10 requests per 5 minutes, on `LoginModel`.
   - `docUpload`: 10 requests per minute, on the DocumentRepository `UploadModel`.

   Queue limit is 0 and rejected requests get the default 503.
10. **HSTS and forwarded headers.** HSTS is 365 days with preload and subdomains. Forwarded headers trust `X-Forwarded-For` and `X-Forwarded-Proto` from any source, because known networks and proxies are cleared.
11. **Services and options.** Everything else is registered here, including the extension methods `AddSearchV2`, `AddProjectPublications` and `AddMediaLibrary`. Many options classes use `ValidateOnStart`, so invalid configuration stops startup.
12. **Request size limit.** `UploadRequestLimitResolver.Resolve` takes the largest configured upload limit and applies it to `FormOptions`, IIS and Kestrel.
13. **Email.** `SmtpEmailSender` is used when `Email:Smtp:Host` is set, otherwise `NoOpEmailSender`.
14. **Razor Pages.** The conventions below are applied and `EnforcePasswordChangeFilter` is added globally.
15. **Build, then the database startup gate.** See [Database startup gate](#database-startup-gate).
16. **`--backfill-forecast`.** If this switch is present, `ForecastBackfillService.BackfillAsync` runs after the gate and the process exits.
17. **HTTP pipeline and endpoint mapping.** See below.
18. **Post-migration work.** This runs before `app.Run()`:
    - Development only: logs the database version.
    - When `Database:RunSeedersOnStartup` is true: runs `StageFlowSeeder` and `IdentitySeeder`.
    - Always: runs `IArppIpaStageSynchronizer.SynchronizeAllAsync()`, the idempotent IPA-stage repair, which writes audit entries.
19. **`app.Run()`.** Hosted services start only at this point, after the gate and the post-migration work have finished.

### Razor Pages conventions

There is **no global fallback authorization policy**. Pages are anonymous unless one of the following applies:

- A folder convention covers them:
  - `AuthorizeFolder("/Dashboard")`
  - `AuthorizeFolder("/Projects/Publications")`
  - `AuthorizeAreaFolder("Admin", "/")`, which only requires an authenticated user; each Admin page adds its own `AdminPolicies.*` policy
  - `ProjectOfficeReports` `/Visits` → `ViewVisits`
  - `ProjectOfficeReports` `/Training` → `ViewTrainingTracker`
  - `ProjectOfficeReports` pages `/Visits/New` and `/Visits/Edit` → `ManageVisits`
  - `ProjectOfficeReports` pages `/SocialMedia/Create`, `/SocialMedia/Edit` and `/SocialMedia/Delete` → `ManageSocialMediaEvents`
- The page model carries `[Authorize]`. Almost every page model does. The exceptions are the anonymous pages below plus `Error` and `Developer`.

Explicitly anonymous pages: `/Index` and Identity `/Account/Login`. The code also has an `AllowAnonymousToPage("/Privacy")` convention, but no such page exists. Extra routes: `/ProjectOfficeReports/Patent` and `/ProjectOfficeReports/Patent/Manage` map to the IPR pages.

### EnforcePasswordChangeFilter

`Infrastructure/EnforcePasswordChangeFilter` is an `IAsyncPageFilter`. It redirects an authenticated user whose `MustChangePassword` is true to `/Identity/Account/Manage/ChangePassword`. It skips `/Identity/Account/Login`, `/Identity/Account/Logout`, the change-password page, `/css` and `/js`.

It applies to **Razor Pages only**. Controllers, minimal APIs and the hub are not gated.

## HTTP pipeline (actual order)

1. `UseForwardedHeaders`
2. Development: `UseDeveloperExceptionPage`. Otherwise: `UseExceptionHandler("/Error")` and `UseHsts`.
3. `UseHttpsRedirection`
4. Inline security-header middleware:
   - Sets on every response: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, and a `Permissions-Policy` that disables camera, microphone, geolocation and browsing-topics.
   - **Document preview contexts** are `/Projects/Documents/View`, `/Projects/Documents/Preview`, `/DocumentRepository/Documents/View`, `/DocumentRepository/Documents/Reader`, `/files`, and `/ProjectIdeas/Details?handler=Preview`. These get:
     - `COOP: unsafe-none`
     - `CORP: cross-origin`
     - `CSP: frame-ancestors 'self'; object-src 'none'`
   - **All other responses** get `COOP` and `CORP` set to `same-origin` and a full CSP:
     - `default-src 'self'`, `script-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`
     - `style-src-attr 'unsafe-inline'` everywhere
     - `'unsafe-inline'` in `style-src` only in Development and under `/Calendar` and `/Projects/Meta/Edit`
     - Extra sources come from `Security:Csp:{ConnectSrcExtra,ImgSrcExtra,StyleSrcExtra,FontSrcExtra}`, plus the legacy key `SecurityHeaders:ContentSecurityPolicy:ConnectSources` for `connect-src`. In Development, loopback HTTPS origins are appended (`ResolveDevelopmentLoopbackOrigins`).
5. `UseStaticFiles`: adds `.geojson` and `.topojson` MIME types. In Development, `/js` and `/css` are served no-store.
6. `UseRouting`
7. Antiforgery header shim: copies a legacy `RequestVerificationToken` header into `X-CSRF-TOKEN` when the latter is absent.
8. `UseRateLimiter`
9. `UseAuthentication`, then `UseAuthorization`
10. `ErpUsageMiddleware`: records authenticated navigation to known ERP modules after a successful HTML response.
11. `UseAntiforgery`
12. Endpoints, in mapping order:
    1. Hub
    2. Controllers
    3. `/api/usage/heartbeat`
    4. Calendar
    5. Notifications
    6. `/api/projects`
    7. Process APIs
    8. Proliferation
    9. Lookups
    10. Categories
    11. Project document view/download
    12. Remarks
    13. Analytics
    14. Mentions
    15. Razor Pages
    16. Celebrations

## Roles

Canonical role names are in `Configuration/RoleNames.cs`. `RoleNames.AssignableRoles` is what Administration offers and what `IdentitySeeder` creates:

| Constant | Role name |
|---|---|
| `Admin` | Admin |
| `Comdt` | Comdt |
| `HoD` | HoD |
| `ProjectOfficer` | Project Officer |
| `ProjectOffice` | Project Office |
| `Mco` | MCO |
| `Ta` | TA |
| `Ito` | ITO |
| `MainOfficeClerk` | Main_Office_Clerk |
| `McCellClerk` | MC_Cell_Clerk |
| `ItCellClerk` | IT_Cell_Clerk |

Legacy aliases are still honoured by many policies but are not assignable: `ProjectOffice` maps to `Project Office`, and `Main Office` maps to `Main_Office_Clerk` (`RoleNames.Canonicalize`).

### Identity seeding

`Data/IdentitySeeder.SeedAsync` runs only when `Database:RunSeedersOnStartup=true`, in any environment. It:

- Creates any missing assignable roles.
- Ensures a bootstrap admin exists. The username comes from `Security:BootstrapAdminUserName` (default `admin`). The password comes from `Security:BootstrapAdminPassword` or the `PRISM_BOOTSTRAP_ADMIN_PASSWORD` environment variable. The account is created with `MustChangePassword=true`. If no admin exists and no password is supplied, startup fails.
- Restores the Admin role to that account if it has lost it.
- **Development only:** creates `test_hod` (HoD) and `test_project_offr` (Project Officer), but only when `DevelopmentSeedUsers:TestHoD:Password` / `DevelopmentSeedUsers:TestProjectOfficer:Password` are configured.

The shipped `appsettings*.json` files all set `RunSeedersOnStartup=false`. `IsoCountrySeeder` runs under the same flag, inside the startup gate.

## Authorization policies

All policies are registered in `builder.Services.AddAuthorization(...)` in `Program.cs`. Role arrays live in `Configuration/Policies.cs`, `Areas/ProjectOfficeReports/Application/ProjectOfficeReportsPolicies.cs` and `Services/Admin/AdminCapabilityCatalog.cs`. "Authenticated" means `RequireAuthenticatedUser`.

### Administration (`AdminCapabilityCatalog.RegisterPolicies`)

| Policy | Roles |
|---|---|
| `Admin.Access`, `Admin.Users.Manage`, `Admin.AccessGovernance.View`, `Admin.Security.View`, `Admin.Logs.View`, `Admin.Recovery.Manage`, `Admin.MasterData.Manage`, `Admin.MasterData.Integrity.Manage`, `Admin.Ingestion.Manage` | Admin |
| `Admin.ActivityTypes.Manage`, `Admin.Holidays.Manage` | Admin, HoD |
| `Admin.Media.Manage`, `Admin.Media.View`, `Admin.Media.Configure`, `Admin.Media.OperateQueue`, `Admin.Media.Recover`, `Admin.Media.Classification.Manage` | Admin, HoD |

The catalogue also describes two capabilities it does not register itself (`ExternalCapability`): `ERP.Usage.View` and `Calendar.ManageCelebrations`. Both are registered directly in `Program.cs`.

### Core policies (`Configuration/Policies.cs`)

| Policy | Requirement |
|---|---|
| `ERP.Usage.View` | Admin, Comdt, HoD |
| `Project.Create` | Admin, HoD |
| `Calendar.ManageEvents` | Admin, HoD, TA, Comdt, MCO, Project Officer, Project Office, ProjectOffice |
| `Calendar.ManageCelebrations`, `Calendar.ManageBirthdays`, `Calendar.ManageAnniversaries` | Admin, TA, Main_Office_Clerk, Main Office |
| `Checklist.View` | Authenticated |
| `Checklist.Edit` | MCO, HoD (not Admin) |
| `Checklist.PurposeEdit` | Admin, HoD |
| `Ipr.View` | Authenticated |
| `Ipr.Edit` | Admin, HoD, ProjectOffice, Project Office |
| `IndustryPartners.View`, `IndustryPartners.Contact.Add` | Authenticated |
| `IndustryPartners.Create` | Admin, HoD, Comdt, Project Officer, Project Office, ProjectOffice, MCO, TA, ITO |
| `IndustryPartners.EditAny`, `IndustryPartners.Contact.ManageAny` | Admin, HoD, Comdt |
| `IndustryPartners.Delete` | Admin, HoD |
| `DocRepo.View` | Authenticated |
| `DocRepo.Upload`, `DocRepo.SoftDelete` | Project Office, Main_Office_Clerk, MC_Cell_Clerk, IT_Cell_Clerk, Admin, HoD |
| `DocRepo.EditMetadata` | Admin, TA, ITO, MCO, HoD |
| `DocRepo.DeleteApprove` | Admin, HoD |
| `DocRepo.ManageCategories`, `DocRepo.Purge` | Admin |
| `ActionTracker.Access` | Comdt, HoD, Project Officer, MCO, TA, ITO |
| `ProjectBriefingDecks.Manage` | Comdt, HoD |
| `ConferenceRemarks.Manage` | Comdt, HoD |

Shared Brochure/Compendium management is **not** a registered policy. It is checked in code through `Policies.Publications.CanManageSharedPublications`, which allows Comdt, HoD and ITO.

### Project Office Reports (`ProjectOfficeReportsPolicies`)

| Policy (`ProjectOfficeReports.*`) | Requirement |
|---|---|
| `ManageFfc` | Admin, HoD, Comdt, ITO |
| `InlineEditFfc` | Admin, HoD, Comdt |
| `ViewVisits` | Authenticated |
| `ManageVisits`, `ManageSocialMediaEvents` | Admin, HoD, ProjectOffice, Project Office |
| `ViewTotTracker` | Authenticated |
| `ManageTotTracker` | Admin, HoD, ProjectOffice, Project Office, Project Officer |
| `ApproveTotTracker` | Admin, HoD |
| `ViewTrainingTracker` | Admin, HoD, ProjectOffice, Project Office, Project Officer, Comdt, MCO, TA, Main_Office_Clerk, Main Office |
| `ManageTrainingTracker` | Admin, HoD, ProjectOffice, Project Office |
| `ApproveTrainingTracker` | Admin, HoD |
| `ViewProgressReview` | Admin, HoD, ProjectOffice, Project Office, Comdt |
| `ViewProliferationTracker` | Authenticated |
| `SubmitProliferationTracker`, `ManageProliferationPreferences` | Admin, HoD, ProjectOffice, Project Office |
| `ApproveProliferationTracker` | Admin, HoD |
| `ViewArpp` | Admin, HoD, Comdt, ProjectOffice, Project Office, MCO, Project Officer |
| `ManageArpp` | Admin, HoD, ProjectOffice, Project Office |
| `VerifyArpp` | Admin, HoD, Comdt |
| `UnlockArpp` | Admin, HoD |

Other role checks that live in code rather than policies:

- `ApprovalAuthorization.CanApproveProjectChanges`: Admin or HoD.
- `IsProjectArchiveActor` in `Program.cs`: Admin or HoD.
- `ProjectAccessGuard.CanViewProjectInformation`: project-level visibility.
- Per-feature permission services, such as `ProjectIdeaPermissionService`.

## HTTP APIs

### Minimal APIs mapped in `Program.cs`

| Route | Methods | Authorization | Notes |
|---|---|---|---|
| `/api/usage/heartbeat` | POST | Authenticated | Validates antiforgery manually |
| `/calendar/events` | GET | Authenticated | `start`/`end` window clamped to 400 days; recurrence expanded; optional `includeCelebrations` |
| `/calendar/events/holidays` | GET | Authenticated | |
| `/calendar/events/{id}` | GET | Authenticated | |
| `/calendar/events` | POST | `Calendar.ManageEvents` | |
| `/calendar/events/{id}` | PUT, DELETE | `Calendar.ManageEvents` | Delete is a soft delete |
| `/calendar/events/preferences/show-celebrations` | POST | Authenticated | |
| `/calendar/events/{id}/task` | POST | Authenticated | Creates a to-do |
| `/calendar/events/celebrations/{id}/task` | POST | Authenticated | Creates a to-do |
| `/api/projects/{id}/archive`, `/restore-archive`, `/trash` | POST | Authenticated, plus an in-handler Admin/HoD check | `ProjectModerationService` |
| `/api/projects/{id}/restore-trash`, `/purge` | POST | `Admin.Recovery.Manage` | |
| `/api/processes/{version}/flow` | GET | `Checklist.View` | |
| `/api/processes/{version}/stages/{stageCode}/checklist` | GET | `Checklist.View` | |
| `.../checklist/purpose` | PUT | `Checklist.PurposeEdit` | |
| `.../checklist` | POST | `Checklist.Edit` | Row-version checks |
| `.../checklist/{itemId}` | PUT, DELETE | `Checklist.Edit` | Row-version checks |
| `.../checklist/reorder` | POST | `Checklist.Edit` | Row-version checks |
| `/api/proliferation/effective` | GET | `ProjectOfficeReports.ViewProliferationTracker` | |
| `/api/lookups/{sponsoring-units,projects,line-directorates,training-types}` | GET | Roles Admin, HoD, Project Officer, Project Office, Comdt, TA, MCO | |
| `/api/categories/children` | GET | `Project.Create` | |
| `/Projects/Documents/View` | GET | Anonymous endpoint; handler requires a signed-in user or a valid preview token, then `ProjectAccessGuard` | Inline, ETag, private cache for 7 days. Only published, non-archived documents |
| `/Projects/Documents/Download` | GET | Same as View | Served as an attachment |
| `/celebrations/upcoming` | GET | Authenticated | |
| `/celebrations/{id}/task` | POST | Authenticated | Validates antiforgery manually |

### Minimal APIs mapped from `Features/`

| Map method | Route | Authorization |
|---|---|---|
| `MapNotificationApi` (`Features/Notifications/NotificationApi.cs`) | `/api/notifications`: GET `/`, GET `/count`, POST `/read`, `/unread`, `/read-all`, `/seen`, POST/DELETE `/projects/{projectId}/mute`, POST/DELETE `/{id}/read` | Authenticated; every mutation validates antiforgery |
| `MapRemarkApi` (`Features/Remarks/RemarkApi.cs`) | `/api/projects/{projectId}/remarks`: POST, GET, PUT/DELETE `/{remarkId}`, GET `/{remarkId}/audit` | Authenticated, with role and project rules in `RemarkService` |
| `MapProjectAnalyticsApi` (`Features/Analytics/ProjectAnalyticsApi.cs`) | `/api/analytics/projects/{category-share,stage-distribution,slip-buckets,top-overdue}` | Authenticated |
| `MapMentionApi` (`Features/Users/MentionApi.cs`) | `/api/users/mentions` | Authenticated |

### Controllers

| Controller | Route | Authorization | Antiforgery |
|---|---|---|---|
| `Controllers/FilesController` | GET `/files`, `/files/{token}` (`?t=`, `?mode=inline`) | Authenticated, plus a data-protected token (`FileAccessTokenService`) bound to the user when it carries a user id | n/a (GET) |
| `Controllers/Api/NotebookController` | `/api/notebook/items/...`, `/api/notebook/{counts,labels,order,trash}` | Authenticated | `[AutoValidateAntiforgeryToken]` |
| `Controllers/Api/NotebookSystemItemsController` | `/api/notebook/system-items/{key}` (GET, PATCH, PUT `placement`) | Roles Comdt, HoD | `[AutoValidateAntiforgeryToken]` |
| `Controllers/ArppProjectLookupController` | GET `/api/arpp/projects` | `ViewArpp` | n/a |
| `Controllers/IndustryPartnersProjectLookupController` | GET `/api/industry-partners/projects` | `IndustryPartners.View` | n/a |
| `Areas/ProjectOfficeReports/Api/ProliferationController` | `/api/proliferation/...` | Per action: View / Submit / Approve / ManagePreferences | `[AutoValidateAntiforgeryToken]` |
| `Areas/ProjectOfficeReports/Api/ProliferationAnalysisController` | POST `/api/proliferation/reports/analysis`, `/export` | `ViewProliferationTracker` | `[ValidateAntiForgeryToken]` |
| `Areas/ProjectOfficeReports/Api/ProliferationReportsController` | GET `/api/proliferation/reports/{unit-suggestions,run,export}` | `ViewProliferationTracker` | n/a |

The MVC `ApiBehaviorOptions` returns a stable `notebook_validation_failed` shape for invalid models under `/api/notebook/`. Enums serialise as strings for both minimal APIs and controllers.

## SignalR

- **Hub:** `Hubs/NotificationsHub` is mapped at `/hubs/notifications` with `RequireAuthorization()`, and the class also carries `[Authorize]`.
- **Hub methods:** `RequestUnreadCount` and `RequestRecentNotifications(limit ≤ 100)`.
- **Client contract (`INotificationsClient`):** includes `ReceiveUnreadCount`, `ReceiveNotification`, `ReceiveNotifications` and `ReceiveNotificationStateChanged`.
- **Server settings:** KeepAlive 15 s, ClientTimeout 90 s, Handshake 15 s. Detailed errors only in Development.
- **Push path:** `NotificationDispatcher` pushes through `IHubContext<NotificationsHub, INotificationsClient>`.

## Background services

Hosted services start after the database gate and the post-migration work, when `app.Run()` is reached. .NET 8 defaults to `BackgroundServiceExceptionBehavior.StopHost` and the app does not override it, so an unhandled exception escaping `ExecuteAsync` stops the whole process.

| Service | Registered by | Condition | Purpose |
|---|---|---|---|
| `Hosted/DocRepoOcrWorker` | `Program.cs` | `DocRepo:EnableOcrWorker` (default true) | OCR pending document-repository files |
| `Hosted/ProjectDocumentOcrWorker` | `Program.cs` | `ProjectDocuments:Ocr:EnableWorker` (default true) | OCR and text extraction for published project documents |
| `Hosted/OcrTextBackfillWorker` | `Program.cs` | `OcrBackfill:Enabled` (default false) | One-shot OCR text backfill |
| `Services/UserPurgeWorker` | `Program.cs` | always | Hard-deletes users past the `UserLifecycle` undo window, polling every minute |
| `Services/Usage/UserActivityRetentionWorker` | `Program.cs` | always | Trims detailed ERP usage buckets (30–1095 days) |
| `Hosted/NotebookTrashRetentionWorker` | `Program.cs` | always | Purges expired notebook trash (`NotebookTrashOptions`) |
| `Services/LoginAggregationWorker` | `Program.cs` | always | Daily login aggregation |
| `Services/TodoPurgeWorker` | `Program.cs` | always | Daily to-do purge (`Todo` options) |
| `Services/Projects/ProjectRetentionWorker` | `Program.cs` | always | Daily purge of projects in Trash past `Projects:Retention:TrashRetentionDays` |
| `Services/Projects/ProjectContentAuditQueue` | `Program.cs` (singleton, also `IProjectContentAuditQueue`) | always | Channel-backed asynchronous audit writer |
| `Services/Notifications/NotificationDispatcher` | `Program.cs` | always | Claims outbox dispatches, creates notifications, pushes to the hub |
| `Services/Notifications/NotificationRetentionService` | `Program.cs` | always | Notification retention (`Notifications:Retention`) |
| `Hosted/AuditRetentionWorker` | `Program.cs` | always; does nothing unless `AuditRetention` is enabled (minimum 30 days) | Audit log retention |
| `SearchIndexWorker` | `AddSearchV2` | worker exits if Search V2 is disabled | Builds, incrementally updates and reconciles the Search V2 index |
| `SearchTelemetryRetentionWorker` | `AddSearchV2` | exits if Search V2 is disabled | Prunes search query logs |
| `PublicationFontWarmupHostedService` | `AddProjectPublications` | always | Registers QuestPDF fonts at start. Failure aborts startup |
| `PublicationRuntimeValidationHostedService` | `AddProjectPublications` | always | Resolves the Brochure and Compendium service graphs at start. Failure aborts startup |
| `PrismMediaOutboxWorker` | `AddMediaLibrary` | catalogue enabled and `SynchronizePrismMedia` | Syncs PRISM media into the catalogue |
| `MediaSourceScannerWorker` | `AddMediaLibrary` | catalogue enabled and (sync or scanner worker enabled) | Scans configured sources |
| `MediaProcessingWorker`, `MediaAvailabilityReconciliationWorker` | `AddMediaLibrary` | any processing worker enabled | Derivatives and availability |
| `FaceAnalysisQueueWorker` (+ `FaceCandidateRefreshWorker`, `FaceIdentityGroupingRefreshWorker`) | `AddMediaLibrary` | People worker enabled (+ candidate search / grouping enabled) | Face intelligence |

Several workers report to an optional worker-status service (`MarkStarted`/`MarkSucceeded`/`MarkFailed`) surfaced in Admin diagnostics. `MediaLibrarySchemaInitializerWorker` exists in source but is not registered.

## Database startup gate

`Program.cs` (section "Database startup policy") runs this before any request is served or any hosted service starts.

### ApplicationDbContext (fatal)

1. Loads `Migrations/immutable-migration-ids.txt` through `MigrationLineageManifest.LoadRequired`, falling back to the embedded resource `ProjectManagement.Migrations.immutable-migration-ids.txt`. The manifest must be non-empty, sorted, unique and in the `yyyyMMddHHmmss_Name` format.
2. Calls `DatabaseStartupMigrator.ApplyDeploymentBoundaryAsync` with a single plan. For PostgreSQL this:
   - Opens a dedicated non-pooled connection and takes the session advisory lock `PRISM_ERP_EF_MIGRATIONS` (`pg_try_advisory_lock`, polled every second, 10-minute timeout).
   - Runs preflight. The discovered migrations must be non-empty and unique and must equal the manifest exactly. No applied migration may be unknown, because downgrades are refused.
   - Runs `MigrateAsync` with a 600 s command timeout.
   - Verifies closure (every known migration is applied).
   - Runs the schema validation callback, then re-checks history and releases the lock.
3. The schema validation callback consists of:
   - `ApplicationDatabaseSchemaValidator.ValidateAsync`: the `ProjectStages` columns and the `CK_ProjectStages_CompletedHasDate` constraint.
   - `EnsureNotebookVersionSchemaAsync`
   - `EnsureDocRepoFavouritesSchemaAsync`
   - `ProjectDocumentSearchVectorMaintenance.ValidateAsync`

`Database:ApplyMigrationsOnStartup` is hard-wired to mandatory. Setting it to `false` only logs a deprecation warning.

### MediaLibraryDbContext (non-fatal, when `MediaLibrary:Enabled`, default true)

1. Runs the same procedure in a **separate** `ApplyDeploymentBoundaryAsync` call, which takes its own lock acquisition. It uses `Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt` and validates through `IMediaLibrarySchemaService.GetStatusAsync`.
2. On failure it logs Critical and writes a diagnostic file, and **the core ERP still starts**. Photos and the media workers remain unavailable, because they check schema readiness themselves.

### Seeding and failure handling

When `Database:RunSeedersOnStartup` is true, `IsoCountrySeeder` runs inside the gate.

Any failure of the application gate writes `startup-diagnostics/startup-failure-*.log` via `StartupFailureReporter`, under `Storage:DataRoot` when it is an absolute path or otherwise under the content root. It then rethrows, so the process does not start.

Runtime code never calls `Database.Migrate()`. See [MIGRATIONS-POLICY.md](../MIGRATIONS-POLICY.md).

## Environment-specific behaviour

| Concern | Development | Other environments |
|---|---|---|
| Exception handling | Developer exception page | `/Error` + HSTS |
| Cookie names / Secure | `PMAuth`, `PMAntiforgery`; SameAsRequest | `__Host-` prefixed; Always Secure |
| Data-protection key default | LocalAppData `PRISM-ERP/DataProtectionKeys` | `/var/pm/keys` |
| CSP | `'unsafe-inline'` styles plus loopback origins | Inline styles only on `/Calendar` and `/Projects/Meta/Edit` |
| Static `/js`, `/css` | no-store | normal caching (`asp-append-version`) |
| SignalR detailed errors | on | off |
| Identity test users | created if passwords configured and seeders enabled | never |
| Superuser warning | – | logs a warning if the DB user is `postgres` |
