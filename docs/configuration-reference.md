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
| `Notifications:Retention:MaxPerUser` | `200` | Hard cap on stored notifications per user; the retention worker trims the oldest extras. |

## Uploads (photos & documents)

| Key | Default | Purpose |
| --- | --- | --- |
| `Storage:DataRoot` | `App_Data` beneath the application base path | Authoritative parent directory for all persisted uploads and document repository files. Override in `appsettings.Production.json` or via the `PM_DATA_ROOT` environment variable to point at a dedicated data volume. |
| `ProjectPhotos:StorageRoot` | `wwwroot/uploads` (resolved at runtime) | Base directory for photo derivatives. Can be overridden via configuration or the `PM_UPLOAD_ROOT` environment variable. (see Services/Projects/ProjectPhotoOptionsSetup.cs lines 1-33) (see Services/Storage/UploadRootProvider.cs lines 17-60) |
| `PM_UPLOAD_ROOT` (env) | empty | When set, overrides `ProjectPhotos:StorageRoot` for all upload consumers (photos, documents, comment attachments). |
| `DocRepo:RootPath` | `App_Data/DocRepo` | Root directory for the document repository. Configure it under the same `Storage:DataRoot` tree so file backups capture all user documents. |
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
| `ProjectDocuments:MaxSizeMb` | `25` | Maximum upload size for project documents. |
| `ProjectDocuments:AllowedMimeTypes` | `application/pdf` | Whitelist of acceptable document MIME types. |
| `ProjectDocuments:EnableVirusScan` | `false` | When true, `DocumentService` expects an `IVirusScanner` to be registered to scan uploads. (see appsettings.json lines 55-65) (see Services/ProjectCommentService.cs lines 174-238) |
| `ProjectVideos:MaxFileSizeBytes` | `209715200` (200 MB) | Maximum raw upload size for project videos. Larger files are rejected before writing to disk. (see appsettings.json lines 73-80) (see Services/Projects/ProjectVideoService.cs lines 82-160) |
| `ProjectVideos:AllowedContentTypes` | `video/mp4`, `video/webm`, `video/ogg` | Video MIME whitelist enforced during uploads. |
| `ProjectVideos:PosterFileExtension` | `.jpg` | File extension used when generating poster frames for videos. |
| `IprAttachments:MaxFileSizeBytes` | `20971520` (20 MB) | Cap on PDF attachments stored against IPR records. (see appsettings.json lines 38-44) (see Application/Ipr/IprAttachmentStorage.cs lines 28-80) |
| `IprAttachments:AllowedContentTypes` | `application/pdf` | MIME types permitted for IPR evidence uploads. |
| `IprAttachments:StorageFolderName` | `ipr-attachments` | Folder created beneath the shared upload root for storing IPR attachments; override to relocate the module or align with external mounts. |
| `ProjectOfficeReports:VisitPhotos:StoragePrefix` | `project-office-reports/visits` | Prefix used when constructing visit photo storage keys. Override to relocate visit assets within the upload root. (see appsettings.json lines 80-94) (see Areas/ProjectOfficeReports/Application/VisitPhotoService.cs lines 363-368) |
| `ProjectOfficeReports:SocialMediaPhotos:StoragePrefix` | `org/social/{eventId}` | Folder template for event-specific social media assets. `{eventId}` is replaced with the event identifier, allowing per-event isolation under the shared upload root. (see appsettings.json lines 94-105) (see Areas/ProjectOfficeReports/Application/SocialMediaPhotoOptions.cs lines 6-37) |
| `FileDownload:TokenLifetimeMinutes` | `30` | Lifetime (minutes) for signed download URLs served via `/files/{token}`. Increase cautiously to balance convenience with replay risk. |
| `FileDownload:BindTokensToUser` | `true` | When true, download tokens are tied to the requesting user ID; set to `false` only if generating system-to-system links is required. |

`ProjectOfficeReports:SocialMediaPhotos` mirrors the `VisitPhotos` block: override `MaxFileSizeBytes`, opt into `MinWidth` / `MinHeight` thresholds when desired, manage `AllowedContentTypes`, and adjust derivative presets per campaign. Keep the key names stable so generated assets continue to resolve even if you tweak dimensions or JPEG quality. The `StoragePrefix` placeholder can be replaced with nested folder names, but must retain an `{eventId}` token so uploads remain segregated per event. (see appsettings.json lines 94-105) (see Areas/ProjectOfficeReports/Application/SocialMediaPhotoOptions.cs lines 6-37)

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
