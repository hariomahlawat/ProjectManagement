# Configuration reference

This document lists every supported configuration value, its default, and how the application uses it. Values come from `appsettings.json`, environment variables, or command-line switches. Settings follow .NET's configuration precedence: environment variables override `appsettings.{Environment}.json`, which override `appsettings.json`.

## Core settings

| Key | Default | Purpose |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Database=ProjectManagement;Username=postgres;Password=postgres` | PostgreSQL connection string used by `ApplicationDbContext`. Overrides are also read from the `ConnectionStrings__DefaultConnection` or `DefaultConnection` environment variables. (see appsettings.json lines 2-5) (see Program.cs lines 47-78) |
| `DP_KEYS_DIR` (env) | `./keys` in Development, `/var/pm/keys` otherwise | Location where ASP.NET Core Data Protection keys are persisted so that cookie encryption stays stable across restarts. (see Program.cs lines 29-45) |
| `--backfill-forecast` (CLI switch) | — | Running `dotnet run -- --backfill-forecast` performs a one-off forecast backfill and exits, useful after changing schedule rules. (see Program.cs lines 229-252) |

## Logging

| Key | Default | Purpose |
| --- | --- | --- |
| `Logging:LogLevel:Default` | `Information` | Baseline log level for the app. |
| `Logging:LogLevel:Microsoft` | `Warning` | Suppresses verbose framework logs. |
| `Logging:LogLevel:ProjectManagement.Services.TodoService` | `None` | Keeps the todo widget quiet unless explicitly enabled. (see appsettings.json lines 6-18) |
| `Logging:LogLevel:ProjectManagement.Services.TodoPurgeWorker` | `Warning` | Only emits warnings/errors from the purge worker to highlight issues. (see appsettings.json lines 6-18) |

## Email

| Key | Default | Purpose |
| --- | --- | --- |
| `Email:From` | empty | Default From address used by `SmtpEmailSender`. Leave blank to skip email metadata when using the no-op sender. (see appsettings.json lines 19-22) (see Program.cs lines 215-228) |
| `Email:Smtp:Host` | empty | Hostname of the SMTP relay. When blank the platform falls back to `NoOpEmailSender`. |
| `Email:Smtp:Port` | `587` | TCP port for the SMTP server. |
| `Email:Smtp:Username` / `Password` | empty | Credentials for authenticated SMTP connections. |

## Identity and user lifecycle

| Key | Default | Purpose |
| --- | --- | --- |
| `UserLifecycle:HardDeleteWindowHours` | `72` | How long after a delete request the user purge worker waits before hard deleting an account. (see appsettings.json lines 23-28) (see Program.cs lines 108-148) |
| `UserLifecycle:UndoWindowMinutes` | `15` | Time window in which administrators can undo a delete request before the user enters the hard-delete queue. |
| `Security:SessionRevalidationMinutes` | `1` | How often a signed-in user's cookie is re-checked against their security stamp (Identity's default is 30). Disabling an account, resetting its password, changing its roles or requesting its deletion rotates the stamp, so open sessions are signed out, or pick up their new roles, within this many minutes. Each check is one user lookup per signed-in user per interval. Clamped to 1–30. SignalR connections opened before the change stay connected until they reconnect. (see Infrastructure/SessionRevalidation.cs) |
| `Security:LoginRateLimit:PermitLimit` | `100` | Sign-in attempts (POSTs to `/Identity/Account/Login`) allowed per client address in each window. Opening the login page is not throttled. Raise it where many users reach PRISM through one terminal server or proxy. Values are clamped to 1–100,000. The client address is taken from `X-Forwarded-For` when present (forwarded headers are trusted from any sender), so a client can forge it; `ServerPermitLimit` is what bounds the total. (see Infrastructure/LoginRateLimiting.cs) |
| `Security:LoginRateLimit:ServerPermitLimit` | `1000` | Sign-in attempts allowed from all clients together in each window. Only attempts that pass the per-client limit count, so one throttled client cannot use up everyone else's share. Clamped to at least `PermitLimit`. (see Infrastructure/SharedRateLimiting.cs) |
| `Security:LoginRateLimit:WindowMinutes` | `5` | Length of the sign-in throttling window, clamped to 1–60 minutes. A throttled client is sent back to the login page with a "too many sign-in attempts" message; per-account guessing is still bounded by Identity lockout after 5 failures. |
| `Security:DocumentUploadRateLimit:PermitLimit` | `20` | Document Repository uploads (POSTs to `/DocumentRepository/Documents/Upload`) allowed per signed-in user in each window. Opening the upload page is not throttled. Values are clamped to 1–100,000. (see Infrastructure/DocumentUploadRateLimiting.cs) |
| `Security:DocumentUploadRateLimit:ServerPermitLimit` | `200` | Uploads allowed from all users together in each window, counted only after the per-user limit passes. Clamped to at least `PermitLimit`. |
| `Security:DocumentUploadRateLimit:WindowMinutes` | `1` | Length of the upload throttling window, clamped to 1–60 minutes. A throttled user is sent back to the upload page with a "too many uploads" message. |

## Tasks

| Key | Default | Purpose |
| --- | --- | --- |
| `Todo:RetentionDays` | `7` | Number of days completed tasks remain soft-deleted before `TodoPurgeWorker` removes them permanently. (see appsettings.json lines 29-32) (see Program.cs lines 108-148) |
| `Todo:MaxOpenTasks` | `500` | Cap on concurrently open tasks per user; `TodoService.CreateAsync` refuses to add more once the limit is reached and instructs the UI to show an error message. (see appsettings.json lines 29-32) (see Services/TodoService.cs lines 20-78) |

## Notifications

| Key | Default | Purpose |
| --- | --- | --- |
| `Notifications:Retention:SweepInterval` | `00:15:00` (15 minutes) | How frequently `NotificationRetentionService` scans for expired notifications. (see appsettings.json lines 32-37) (see Program.cs lines 206-236) |
| `Notifications:Retention:MaxAge` | `30.00:00:00` (30 days) | Maximum age before a notification becomes eligible for deletion. |
| `Notifications:Retention:MaxPerUser` | `200` | Hard cap on stored notifications per user; the retention worker trims the oldest extras. Unread action-required notifications are exempt while `AutoReadActionRequiredAfter` is enabled. |
| `Notifications:Retention:CompletedDispatchMaxAge` | `14.00:00:00` (14 days) | Age after which delivered outbox dispatch rows are removed. |
| `Notifications:Retention:DeadLetterMaxAge` | `90.00:00:00` (90 days) | Age after which dead-lettered outbox dispatch rows are removed. |
| `Notifications:Retention:AutoReadInformationalAfter` | `7.00:00:00` (7 days) | Unread notifications that are not action-required are marked read once older than this. Rows are kept; only read state changes. Empty or `00:00:00` disables it. |
| `Notifications:Retention:AutoReadActionRequiredAfter` | `30.00:00:00` (30 days) | Unread action-required notifications (approvals, delete requests, mentions, task and stage assignments, blocked tasks, notebook shares; role and project-officer changes are informational) are marked read once older than this. Until then they are exempt from `MaxAge` and `MaxPerUser` deletion. Empty or `00:00:00` disables both the auto-read and the exemption. |

The `MaxAge` exemption means an unread action-required notification can outlive `MaxAge` until it reaches `AutoReadActionRequiredAfter`. Action-required kinds come from `NotificationPresentationCatalog.ActionRequiredKinds`.

**Bell badge.** The bell badge counts notifications the user has not yet *seen*, not unread ones. Opening the bell calls `POST /api/notifications/seen-all`, which clears the badge and leaves read state unchanged. The bell header, the Notification Centre and "Mark all read" continue to use the unread count. The bell lists unread action-required notifications first under "Needs action" (`GET /api/notifications?order=priority`), then everything else newest first.

**Bell grouping.** Routine notifications for the same project, category and India-time day show as one expandable row in the bell, for example "Project Orion · Documents — 6 updates · 5 unread". A notification is groupable only when it is not action-required and has normal priority; approvals, mentions, task and stage assignments, blocked tasks and high-priority items such as document deletions always stay on their own row. The server decides this and sends `groupKey` with each list item (`NotificationGrouping`); the client only collapses rows with equal keys. To build groups from real volume, the bell loads the newest 50 rows and shows 10 entries. For viewers with the Comdt, MCO or HoD role, all of a day's routine remark notifications (`RemarkCreated`) share one group across projects, for example "3 projects · Collaboration — 5 updates", with each row naming its project; other viewers keep one remark group per project. Who receives remark notifications is unchanged, and mentions stay separate, action-required rows. The roles are listed in `NotificationGrouping.CrossProjectRemarkRoles`. The Notification Centre groups the same way over every row loaded so far, so a group can grow when "Load more" brings in older rows. A group row's checkbox selects all of its notifications, and its buttons mark them all read or unread or mute the project. The "1–N of total" range still counts individual notifications. A "Group similar" toolbar button switches the Centre to a flat list; the choice is kept in the browser's local storage (`prism.notifications.centre.grouped`) and defaults to grouped.

**Bell cost.** The bell page takes 6 database round trips and the count endpoint (`GET /api/notifications/count`, also run after every read/seen/mute action) takes 3; `NotificationQueryCostPostgresIntegrationTests` holds those budgets. Unread count, unseen count and the newest id come from one aggregate. Background refreshes (realtime connect and reconnect, polling, tab focus) call the count endpoint first and reload the list only when its `listVersion` differs from the bell's, or at least every 15 minutes; the refresh button always reloads. `listVersion` comes from the same aggregate and changes whenever a row the bell could show is added, deleted (for example by retention), read, unread or seen, including read/unread changes that leave the counts equal. The Notification Centre list is not grouped.

## Chat

| Key | Default | Purpose |
| --- | --- | --- |
| `Chat:Enabled` | `true` | Shows the chat button and dock and serves `/api/chat` and `/hubs/chat`. `false` hides and refuses them without touching stored messages. Read live. See [chat.md](chat.md). |
| `Chat:SendPermitLimit` | `20` | Messages one user may send per `Chat:SendWindowSeconds` (HTTP 429 beyond that). |
| `Chat:SendWindowSeconds` | `10` | Length of the chat send window in seconds. |
| `Chat:UnsendWindowMinutes` | `5` | How long after sending a chat message its sender may unsend it; `0` turns unsend off. |

## Uploads (photos & documents)

| Key | Default | Purpose |
| --- | --- | --- |
| `Storage:DataRoot` | `App_Data` beneath the application base path | Authoritative parent directory for all persisted uploads and document repository files. Override in `appsettings.Production.json` or via the `PM_DATA_ROOT` environment variable to point at a dedicated data volume. |
| `ProjectPhotos:StorageRoot` | `wwwroot/uploads` (resolved at runtime) | Base directory for photo derivatives. Can be overridden via configuration or the `PM_UPLOAD_ROOT` environment variable. (see Services/Projects/ProjectPhotoOptionsSetup.cs lines 1-33) (see Services/Storage/UploadRootProvider.cs lines 17-60) |
| `PM_UPLOAD_ROOT` (env) | empty | When set, overrides `ProjectPhotos:StorageRoot` for all upload consumers (photos, documents, comment attachments). The upload root also holds OCR work files by default (`ocr-work`) and legacy-import previews waiting to be imported (`admin-import-staging`). |
| `DocRepo:RootPath` | `App_Data/DocRepo` | Root directory for the document repository. Configure it under the same `Storage:DataRoot` tree so file backups capture all user documents. |
| `DocRepo:TrashRetentionDays` | `30` | Days a deleted repository document stays in the trash before `DocRepoTrashPurgeWorker` deletes its file and row for good (checked every 6 hours, audited as `Purged` by `system`). `0` turns automatic purging off; any other value must be at least 7. Documents with no `DeletedAtUtc` are never purged automatically. `appsettings.Development.json` sets `0` so local runs never purge local data. (see Services/DocRepo/DocRepoTrashPurgeService.cs, Hosted/DocRepoTrashPurgeWorker.cs) |
| `DocRepo:MaxFileSizeBytes` | `104857600` (100 MB) | **Not read.** The repository upload limit comes from `ProjectDocuments:MaxSizeMb`; this key is kept so existing settings files stay valid. See [Document Repository](document-repository.md#upload-rules). |
| `DocRepo:EnableCovers` | `true` | First-page covers in the library, drawn with Poppler's `pdftoppm` and kept under `<DocRepo:RootPath>/covers`. `false` shows the type-coloured covers only. Without Poppler the library falls back to those automatically. See [Document Repository](document-repository.md#covers). |
| `DocRepo:PdfToPpmPath` | *(unset: `pdftoppm` on `PATH`)* | Full path to Poppler's `pdftoppm` when it is not on the server's `PATH`. |
| `ProjectPhotos:MaxFileSizeBytes` | `5_242_880` (5 MB) | Maximum raw photo upload size. |
| `ProjectPhotos:MinWidth` / `MinHeight` | `720`×`540` | Historical guidance for recommended resolution; uploads smaller than these values are still accepted but may be flagged as low resolution. |
| `ProjectPhotos:AllowedContentTypes` | `image/jpeg`, `image/png`, `image/webp` | MIME types accepted for project photos. |
| `ProjectPhotos:Derivatives` | `xl`, `md`, `sm`, `xs` presets | Derivative dimensions and JPEG quality per size key. |
| `ProjectPhotos:MaxProcessingConcurrency` | `2` | Maximum number of simultaneous image processing operations. |
| `ProjectPhotos:MaxEncodingConcurrency` | `2` | Maximum number of simultaneous encoding operations. |
| `ProjectDocuments:ProjectsSubpath` | `projects` | Root folder name created under the upload root for each project. (see appsettings.json lines 55-65) (see Services/Storage/UploadRootProvider.cs lines 61-100) |
| `ProjectDocuments:PhotosSubpath` | empty | Optional subfolder for project photos (defaults to the project root when blank). |
| `ProjectDocuments:StorageSubPath` | `docs` | Folder under each project where general documents are stored. |
| `ProjectDocuments:CommentsSubpath` | `comments` | Folder used for comment attachments. |
| `ProjectDocuments:TempSubPath` | `temp` | Temporary workspace for document processing. |
| `ProjectDocuments:MaxSizeMb` | `100` | Maximum upload size for project documents. |
| `ProjectDocuments:AllowedMimeTypes` | `application/pdf`, `application/msword`, `application/vnd.ms-powerpoint`, `application/vnd.ms-excel`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.openxmlformats-officedocument.presentationml.presentation`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | Whitelist of acceptable document MIME types. |
| `ProjectDocuments:EnableVirusScan` | `false` | When true, `DocumentService` expects an `IVirusScanner` to be registered to scan uploads. (see appsettings.json lines 55-65) (see Services/ProjectCommentService.cs lines 174-238) |
| `ProjectVideos:MaxFileSizeBytes` | `209715200` (200 MB) | Maximum raw upload size for project videos. Larger files are rejected before writing to disk. (see appsettings.json lines 73-80) (see Services/Projects/ProjectVideoService.cs lines 82-160) |
| `ProjectVideos:AllowedContentTypes` | `video/mp4`, `video/webm`, `video/ogg` | Video MIME whitelist enforced during uploads. |
| `ProjectVideos:PosterFileExtension` | `.jpg` | File extension used when generating poster frames for videos. |
| `IprAttachments:MaxFileSizeBytes` | `20971520` (20 MB) | Cap on PDF attachments stored against IPR records. (see appsettings.json lines 38-44) (see Application/Ipr/IprAttachmentStorage.cs lines 28-80) |
| `IprAttachments:AllowedContentTypes` | `application/pdf` | MIME types permitted for IPR evidence uploads. |
| `IprAttachments:StorageFolderName` | `ipr-attachments` | Folder created beneath the shared upload root for storing IPR attachments; override to relocate the module or align with external mounts. |
| `ProjectOfficeReports:VisitPhotos:StoragePrefix` | `project-office-reports/visits` | Prefix used when constructing visit photo storage keys. Override to relocate visit assets within the upload root. (see appsettings.json lines 80-94) (see Areas/ProjectOfficeReports/Application/VisitPhotoService.cs lines 363-368) |
| `ProjectOfficeReports:SocialMediaPhotos:StoragePrefix` | `org/social/{eventId}` | Folder template for event-specific social media assets. `{eventId}` is replaced with the event identifier, allowing per-event isolation under the shared upload root. (see appsettings.json lines 94-105) (see Areas/ProjectOfficeReports/Application/SocialMediaPhotoOptions.cs lines 6-37) |
| `FileDownload:TokenLifetimeMinutes` | `30` | Lifetime (minutes) for signed download URLs served via `/files?t={token}` (legacy path-segment links remain compatible). Increase cautiously to balance convenience with replay risk. |
| `FileDownload:BindTokensToUser` | `true` | When true, download tokens are tied to the requesting user ID; set to `false` only if generating system-to-system links is required. |

`ProjectOfficeReports:SocialMediaPhotos` mirrors the `VisitPhotos` block: override `MaxFileSizeBytes`, opt into `MinWidth` / `MinHeight` thresholds when desired, manage `AllowedContentTypes`, and adjust derivative presets per campaign. Keep the key names stable so generated assets continue to resolve even if you tweak dimensions or JPEG quality. The `StoragePrefix` placeholder can be replaced with nested folder names, but must retain an `{eventId}` token so uploads remain segregated per event. (see appsettings.json lines 94-105) (see Areas/ProjectOfficeReports/Application/SocialMediaPhotoOptions.cs lines 6-37)

## Project trash and admin recovery

| Key | Default | Purpose |
| --- | --- | --- |
| `Projects:Retention:TrashRetentionDays` | `30` | Days a trashed project stays in Trash. `ProjectRetentionWorker` (daily) purges projects trashed longer ago than this, and an administrator cannot purge a project manually (Admin > Project trash, or `POST /api/projects/{id}/purge`) before it has passed. Values below `1` are treated as `30` and the worker logs a warning, so a mistyped `0` can never purge every trashed project. (see Configuration/ProjectRetentionOptions.cs, Services/Projects/ProjectRetentionWorker.cs) |
| `Projects:Retention:RemoveAssetsOnPurge` | `true` | Whether the daily worker also deletes a purged project's upload directory. The admin Trash page asks per purge and defaults to this value. |
| `AdminRecovery:DueSoonDays` | `7` | Projects this many days or fewer from purge show as "Due soon" in the Recovery centre and the Trash filter. Must be 1–90. |
| `AdminRecovery:DefaultPageSize` / `MaximumPageSize` | `25` / `100` | Page sizes for the recovery lists. Default must be between 10 and the maximum; the maximum must be 10–500. |
| `AdminRecovery:MaximumBulkDocuments` | `100` | Most documents one recycle-bin bulk restore or delete may process. Must be 1–500. |
| `AdminRecovery:RecentOperationCount` | `8` | Rows in the Recovery centre's recent operations list. Must be 1–50. |
| `AdminRecovery:LegacyImportPreviewRows` / `LegacyImportStagingMinutes` | `100` / `30` | Legacy project import preview size (10–1000) and how long a staged workbook is kept (5–240 minutes). |

`AdminRecovery` values are validated at startup (see Configuration/AdminRecoveryOptionsValidator.cs).

A manual purge always needs a reason (5–512 characters) and a project that has been in Trash for the full retention period; a project with no recorded deletion date must be restored and trashed again first. The purge, its reason and the project name are audited as `Projects.Purge` in the same transaction as the deletion.

## Data directories & backup automation

| Key | Default | Purpose |
| --- | --- | --- |
| `PM_DATA_ROOT` (env) | empty | Master directory that the file backup scripts mirror. Point it at the same location as `Storage:DataRoot` to guarantee every upload, DocRepo file, and OCR workspace is captured. |
| `PM_FILE_BACKUP_ROOT` (env) | empty | Destination directory for `ops/backup-files.sh`. The script creates timestamped mirrors beneath this root. |
| `PM_FILE_BACKUP_SOURCE` (env) | empty | Source directory used by `ops/restore-files.sh` to locate the mirror that should be copied back into `PM_DATA_ROOT`. |
| `PM_BACKUP_DIR` (env) | empty | Directory where `ops/backup-db.sh` writes `.dump` files. Commonly `${PM_DATA_ROOT}/backups/db`. |
| `PM_DUMP_FILE` (env) | empty | Absolute path to the `.dump` file consumed by `ops/restore-db.sh`. |
| `PGRESTORE_USER` (env) | empty | PostgreSQL role (usually superuser) used by `ops/restore-db.sh` when recreating the database. |
| `PGBIN` (env) | `/usr/bin` | Optional override for the PostgreSQL binary directory consumed by the Linux scripts. Set it if `pg_dump` / `pg_restore` are not on the default PATH. |
| `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER` (env) | Standard PostgreSQL defaults | Connection metadata used by `ops/backup-db.sh`. Provide these via the job definition rather than editing the script. |

## Security headers & networking

| Key | Default | Purpose |
| --- | --- | --- |
| `SecurityHeaders:ContentSecurityPolicy:ConnectSources` | `[]` | Extra origins appended to the Content Security Policy `connect-src` directive, typically for reverse proxies or analytics endpoints. (see appsettings.json lines 66-70) (see Program.cs lines 229-310) |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` (implicit) | — | When deploying behind a proxy, ensure forwarded headers are enabled; the app clears known networks so only explicit proxy config is used. (see Program.cs lines 125-148) |

## Calendar

Calendar behaviour is driven by APIs rather than configuration. However, the following environment value is commonly tuned:

| Key | Default | Purpose |
| --- | --- | --- |
| `TimeZone` (runtime) | Asia/Kolkata | Many helpers rely on `IstClock` which is fixed to IST; adjust application logic if another timezone is required. (see Infrastructure/IstClock.cs lines 1-34) |

## Photo Tools: Remove background

`PhotoTools:BackgroundRemoval` turns on Remove background in Photo Tools and points it at the
approved model. It is off by default (`Enabled: false`); the model is installed separately and
must match `Model:Sha256`. The full list of keys, the model values and the install steps are in
[photo-tools-background-removal.md](photo-tools-background-removal.md).

| Key | Default | Purpose |
| --- | --- | --- |
| `PhotoTools:BackgroundRemoval:Enabled` | `false` | Turns the tool on. |
| `PhotoTools:BackgroundRemoval:ModelRoot` | empty | Folder holding the model file. |
| `PhotoTools:BackgroundRemoval:Model:FileName` | `background_removal_bria_rmbg_2.0.onnx` | The model file. |
| `PhotoTools:BackgroundRemoval:Model:Sha256` | empty | Required SHA-256 of the file. |
| `PhotoTools:BackgroundRemoval:IntraOpThreads` | `6` | Processor threads one photo may use; all photos together never use more than half the processors. |
| `PhotoTools:BackgroundRemoval:ModelIdleMinutes` | `10` | Minutes the model stays in memory after its last photo; `0` keeps it. Released at once when the tool is turned off. |
| `PhotoTools:BackgroundRemoval:MaximumConcurrent` / `MaximumWaiting` | `1` / `4` | Photos at once (1 to 4), and waiting (0 to 32), before "busy". |
| `PhotoTools:BackgroundRemoval:TimeoutSeconds` | `60` | Longest time for one photo. |

## Background services

No additional configuration is required for hosted services beyond the keys listed above, but note the following runtime behaviour:

- `NotificationDispatcher` processes queued notifications continuously and delivers them via SignalR. (see Services/Notifications/NotificationDispatcher.cs lines 21-140)
- `NotificationRetentionService` trims dispatches according to the retention settings listed earlier. (see Services/Notifications/NotificationRetentionService.cs lines 20-147)
- `TodoPurgeWorker` reads `Todo:RetentionDays` and deletes stale completed tasks. (see Services/TodoPurgeWorker.cs lines 14-108)
- `UserPurgeWorker` enforces the user lifecycle window for hard deletes. (see Services/UserPurgeWorker.cs lines 18-120)

## Command-line switches

The executable accepts the following switches:

| Switch | Effect |
| --- | --- |
| `--backfill-forecast` | Runs `ForecastBackfillService.BackfillAsync()` once at startup then exits. Useful when stage templates change and historical projects need recalculated forecast dates. (see Program.cs lines 229-252) |

When hosting with systemd or a container orchestrator, expose these settings as environment variables to avoid modifying the bundled JSON files.
