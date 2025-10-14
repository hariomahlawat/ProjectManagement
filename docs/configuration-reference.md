# Configuration reference

This document lists every supported configuration value, its default, and how the application uses it. Values come from `appsettings.json`, environment variables, or command-line switches. Settings follow .NET's configuration precedence: environment variables override `appsettings.{Environment}.json`, which override `appsettings.json`.

## Core settings

| Key | Default | Purpose |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Database=ProjectManagement;Username=postgres;Password=postgres` | PostgreSQL connection string used by `ApplicationDbContext`. Overrides are also read from the `ConnectionStrings__DefaultConnection` or `DefaultConnection` environment variables.【F:appsettings.json†L2-L5】【F:Program.cs†L47-L78】 |
| `DP_KEYS_DIR` (env) | `./keys` in Development, `/var/pm/keys` otherwise | Location where ASP.NET Core Data Protection keys are persisted so that cookie encryption stays stable across restarts.【F:Program.cs†L29-L45】 |
| `--backfill-forecast` (CLI switch) | — | Running `dotnet run -- --backfill-forecast` performs a one-off forecast backfill and exits, useful after changing schedule rules.【F:Program.cs†L229-L252】 |

## Logging

| Key | Default | Purpose |
| --- | --- | --- |
| `Logging:LogLevel:Default` | `Information` | Baseline log level for the app. |
| `Logging:LogLevel:Microsoft` | `Warning` | Suppresses verbose framework logs. |
| `Logging:LogLevel:ProjectManagement.Services.TodoService` | `None` | Keeps the todo widget quiet unless explicitly enabled.【F:appsettings.json†L6-L18】 |
| `Logging:LogLevel:ProjectManagement.Services.TodoPurgeWorker` | `Warning` | Only emits warnings/errors from the purge worker to highlight issues.【F:appsettings.json†L6-L18】 |

## Email

| Key | Default | Purpose |
| --- | --- | --- |
| `Email:From` | empty | Default From address used by `SmtpEmailSender`. Leave blank to skip email metadata when using the no-op sender.【F:appsettings.json†L19-L22】【F:Program.cs†L215-L228】 |
| `Email:Smtp:Host` | empty | Hostname of the SMTP relay. When blank the platform falls back to `NoOpEmailSender`. |
| `Email:Smtp:Port` | `587` | TCP port for the SMTP server. |
| `Email:Smtp:Username` / `Password` | empty | Credentials for authenticated SMTP connections. |

## Identity and user lifecycle

| Key | Default | Purpose |
| --- | --- | --- |
| `UserLifecycle:HardDeleteWindowHours` | `72` | How long after a delete request the user purge worker waits before hard deleting an account.【F:appsettings.json†L23-L28】【F:Program.cs†L108-L148】 |
| `UserLifecycle:UndoWindowMinutes` | `15` | Time window in which administrators can undo a delete request before the user enters the hard-delete queue. |

## Tasks

| Key | Default | Purpose |
| --- | --- | --- |
| `Todo:RetentionDays` | `7` | Number of days completed tasks remain soft-deleted before `TodoPurgeWorker` removes them permanently.【F:appsettings.json†L29-L32】【F:Program.cs†L108-L148】 |
| `Todo:MaxOpenTasks` | `500` | Cap on concurrently open tasks per user; `TodoService.CreateAsync` refuses to add more once the limit is reached and instructs the UI to show an error message.【F:appsettings.json†L29-L32】【F:Services/TodoService.cs†L20-L78】 |

## Notifications

| Key | Default | Purpose |
| --- | --- | --- |
| `Notifications:Retention:SweepInterval` | `00:15:00` (15 minutes) | How frequently `NotificationRetentionService` scans for expired notifications.【F:appsettings.json†L32-L37】【F:Program.cs†L206-L236】 |
| `Notifications:Retention:MaxAge` | `30.00:00:00` (30 days) | Maximum age before a notification becomes eligible for deletion. |
| `Notifications:Retention:MaxPerUser` | `200` | Hard cap on stored notifications per user; the retention worker trims the oldest extras. |

## Uploads (photos & documents)

| Key | Default | Purpose |
| --- | --- | --- |
| `ProjectPhotos:StorageRoot` | `wwwroot/uploads` (resolved at runtime) | Base directory for photo derivatives. Can be overridden via configuration or the `PM_UPLOAD_ROOT` environment variable.【F:Services/Projects/ProjectPhotoOptionsSetup.cs†L1-L33】【F:Services/Storage/UploadRootProvider.cs†L17-L60】 |
| `PM_UPLOAD_ROOT` (env) | empty | When set, overrides `ProjectPhotos:StorageRoot` for all upload consumers (photos, documents, comment attachments). |
| `ProjectPhotos:MaxFileSizeBytes` | `5_242_880` (5 MB) | Maximum raw photo upload size. |
| `ProjectPhotos:MinWidth` / `MinHeight` | `720`×`540` | Minimum resolution enforced before processing derivatives. |
| `ProjectPhotos:AllowedContentTypes` | `image/jpeg`, `image/png`, `image/webp` | MIME types accepted for project photos. |
| `ProjectPhotos:Derivatives` | `xl`, `md`, `sm`, `xs` presets | Derivative dimensions and JPEG quality per size key. |
| `ProjectPhotos:MaxProcessingConcurrency` | `2` | Maximum number of simultaneous image processing operations. |
| `ProjectPhotos:MaxEncodingConcurrency` | `2` | Maximum number of simultaneous encoding operations. |
| `ProjectDocuments:ProjectsSubpath` | `projects` | Root folder name created under the upload root for each project.【F:appsettings.json†L55-L65】【F:Services/Storage/UploadRootProvider.cs†L61-L100】 |
| `ProjectDocuments:PhotosSubpath` | empty | Optional subfolder for project photos (defaults to the project root when blank). |
| `ProjectDocuments:StorageSubPath` | `docs` | Folder under each project where general documents are stored. |
| `ProjectDocuments:CommentsSubpath` | `comments` | Folder used for comment attachments. |
| `ProjectDocuments:TempSubPath` | `temp` | Temporary workspace for document processing. |
| `ProjectDocuments:MaxSizeMb` | `25` | Maximum upload size for project documents. |
| `ProjectDocuments:AllowedMimeTypes` | `application/pdf` | Whitelist of acceptable document MIME types. |
| `ProjectDocuments:EnableVirusScan` | `false` | When true, `DocumentService` expects an `IVirusScanner` to be registered to scan uploads.【F:appsettings.json†L55-L65】【F:Services/ProjectCommentService.cs†L174-L238】 |
| `ProjectOfficeReports:VisitPhotos:StoragePrefix` | `project-office-reports/visits` | Prefix used when constructing visit photo storage keys. Override to relocate visit assets within the upload root.【F:appsettings.json†L80-L94】【F:Areas/ProjectOfficeReports/Application/VisitPhotoService.cs†L363-L368】 |
| `ProjectOfficeReports:SocialMediaPhotos:StoragePrefix` | `org/social/{eventId}` | Folder template for event-specific social media assets. `{eventId}` is replaced with the event identifier, allowing per-event isolation under the shared upload root.【F:appsettings.json†L94-L107】【F:Areas/ProjectOfficeReports/Application/SocialMediaPhotoOptions.cs†L20-L31】 |

`ProjectOfficeReports:SocialMediaPhotos` mirrors the `VisitPhotos` block: override `MaxFileSizeBytes`, `MinWidth`, `MinHeight`, `AllowedContentTypes`, and individual `Derivatives` entries when tailoring social media exports for a specific campaign. Keep the key names stable so generated assets continue to resolve even if you tweak dimensions or JPEG quality. The `StoragePrefix` placeholder can be replaced with nested folder names, but must retain an `{eventId}` token so uploads remain segregated per event.【F:appsettings.json†L94-L107】【F:Areas/ProjectOfficeReports/Application/SocialMediaPhotoOptions.cs†L6-L31】

## Security headers & networking

| Key | Default | Purpose |
| --- | --- | --- |
| `SecurityHeaders:ContentSecurityPolicy:ConnectSources` | `[]` | Extra origins appended to the Content Security Policy `connect-src` directive, typically for reverse proxies or analytics endpoints.【F:appsettings.json†L66-L70】【F:Program.cs†L229-L310】 |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` (implicit) | — | When deploying behind a proxy, ensure forwarded headers are enabled; the app clears known networks so only explicit proxy config is used.【F:Program.cs†L125-L148】 |

## Calendar

Calendar behaviour is driven by APIs rather than configuration. However, the following environment value is commonly tuned:

| Key | Default | Purpose |
| --- | --- | --- |
| `TimeZone` (runtime) | Asia/Kolkata | Many helpers rely on `IstClock` which is fixed to IST; adjust application logic if another timezone is required.【F:Infrastructure/IstClock.cs†L1-L34】 |

## Background services

No additional configuration is required for hosted services beyond the keys listed above, but note the following runtime behaviour:

- `NotificationDispatcher` processes queued notifications continuously and delivers them via SignalR.【F:Services/Notifications/NotificationDispatcher.cs†L21-L140】
- `NotificationRetentionService` trims dispatches according to the retention settings listed earlier.【F:Services/Notifications/NotificationRetentionService.cs†L20-L147】
- `TodoPurgeWorker` reads `Todo:RetentionDays` and deletes stale completed tasks.【F:Services/TodoPurgeWorker.cs†L14-L108】
- `UserPurgeWorker` enforces the user lifecycle window for hard deletes.【F:Services/UserPurgeWorker.cs†L18-L120】

## Command-line switches

The executable accepts the following switches:

| Switch | Effect |
| --- | --- |
| `--backfill-forecast` | Runs `ForecastBackfillService.BackfillAsync()` once at startup then exits. Useful when stage templates change and historical projects need recalculated forecast dates.【F:Program.cs†L229-L252】 |

When hosting with systemd or a container orchestrator, expose these settings as environment variables to avoid modifying the bundled JSON files.
