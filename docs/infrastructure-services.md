# Infrastructure and Services

This module documents cross-cutting infrastructure and service abstractions.

## Infrastructure

### `Infrastructure/EnforcePasswordChangeFilter.cs`
An `IAsyncPageFilter` applied globally. After authentication, it checks `ApplicationUser.MustChangePassword` and redirects to `/Identity/Account/Manage/ChangePassword` until the password is updated. Login, logout and the change-password page itself are exempt from the check.

### Project photo storage
`ProjectPhotoService` reads its limits from `ProjectPhotoOptions`: max upload size, minimum dimensions, allowed MIME types, the derivative presets that define width, height and quality per size key, and the `StorageRoot` that points to the uploads directory.【F:Services/Projects/ProjectPhotoOptions.cs†L6-L36】  `ProjectPhotoOptionsSetup` fills in a default beneath the ASP.NET Core web/content root (typically `wwwroot/uploads`) so development installs work without extra provisioning, but production operators can override it through configuration or the `PM_UPLOAD_ROOT` environment variable.【F:Services/Projects/ProjectPhotoOptionsSetup.cs†L1-L38】  `UploadRootProvider` centralises those precedence rules, normalises the chosen directory, creates it if necessary, and exposes the resolved path to any upload-capable service so project photos and attachments always share the same root.【F:Services/Storage/UploadRootProvider.cs†L1-L33】  `ProjectPhotoService` then builds deterministic paths under `projects/{projectId}` for every derivative it writes.【F:Services/Projects/ProjectPhotoService.cs†L495-L536】  Operators should ensure the chosen location grants the application user write permissions, keep the `projects` sub-folder seeded (the services create it on demand), and provision enough space for all configured derivatives.【F:Services/Projects/ProjectPhotoService.cs†L458-L512】  If an antivirus engine is available, register an `IVirusScanner` implementation so each upload stream is scanned before processing; otherwise the optional dependency can be left null.【F:Services/Projects/ProjectPhotoService.cs†L44-L55】【F:Services/Projects/ProjectPhotoService.cs†L375-L405】  When rotating configuration, keep the size keys stable so existing derivatives continue to resolve—changes to width/quality only affect images created after the update.

## Services

### `Services/IAuditService` and `AuditService`
Centralised logger that records structured audit entries (timestamp, action, user details, IP and optional data) while scrubbing sensitive fields and skipping noisy Todo events.

### `Services/IUserManagementService.cs`
Defines an abstraction for managing users and roles:
* Query users and roles
* Create users with an initial role
* Update a user's role
* Toggle activation/lockout
* Enforce that at least one administrator remains active. Operations that would disable, delete or remove the Admin role from the last active admin are rejected, and users cannot disable their own account.
* Reset passwords (marking the user for a forced change)
* Delete users

### `Services/UserManagementService.cs`
Concrete implementation backed by `UserManager<ApplicationUser>` and `RoleManager<IdentityRole>`. It encapsulates all identity operations used by the administration UI or other features. Developers can extend this service to add new user-related behaviours such as emailing users after role changes or integrating external identity providers.

### `Services/IUserLifecycleService` and `UserLifecycleService`
Coordinates disabling, enabling and scheduled hard deletes for accounts. Protects against removing the last active admin and logs every operation through `IAuditService`.

### Email senders
* `Services/NoOpEmailSender.cs` – a dummy implementation used when SMTP settings are absent (common on private networks).
* `Services/SmtpEmailSender.cs` – sends HTML email via SMTP using configuration values (`Email:Smtp:Host`, `Port`, `Username`, `Password`, `Email:From`).

### `Services/ITodoService` and `TodoService`
`ITodoService` abstracts operations on personal To-Do items such as creation, completion, pinning, snoozing, reordering and bulk operations. `TodoService` implements the interface using `ApplicationDbContext` for persistence and `IAuditService` for logging. Time calculations are normalised to Indian Standard Time using `TimeZoneHelper` (backed by `TimeZoneConverter`) to ensure consistent due date handling; date-only inputs are coerced to 23:59:59 IST before conversion to UTC. PostgreSQL's `xmin` system column is used to detect concurrent edits.

### `Services/TodoPurgeWorker`
Background worker that permanently deletes soft-deleted to-do items after a retention period. The retention window is configured via `Todo:RetentionDays` in `appsettings.json` (defaults to 7 days) and the worker safely handles cancellation tokens.

### `Services/UserPurgeWorker`
Periodic background service that permanently deletes accounts once their deletion undo window has elapsed.

### Project domain services

#### `Services/Projects/ProjectFactsService.cs`
Central coordinator for procurement facts. Each operation validates inputs, creates or updates the relevant fact row, clears stage backfill flags and records an audit entry describing the change. Monetary facts share an internal helper so concurrency tokens and timestamps stay consistent across cost types.

#### `Services/Projects/ProjectFactsReadService.cs`
Simple guard that checks whether a project has captured the required fact for a given stage code. The timeline and plan approval flows use it to block progression when mandatory data is missing.

#### `Services/Projects/ProjectProcurementReadService.cs`
Aggregates the latest procurement numbers per project by querying each fact table and returning a compact `ProcurementAtAGlanceVm`. Results are ordered by creation time so users always see the most recent value, even if history exists.

#### `Services/Projects/ProjectTimelineReadService.cs`
Builds the read model for the overview timeline, stitching together `ProjectStages`, open plan versions and approval metadata. It flags outstanding backfill requirements, exposes the number of completed stages and provides the friendly names displayed in the UI.

#### `Services/ProjectCommentService.cs`
Handles threaded project conversations, including file uploads. It validates stage ownership, enforces attachment type/size limits, stores metadata for each file on disk and writes audit logs for create/edit/delete actions. File names are sanitised and stored beneath the shared upload root resolved by `IUploadRootProvider`, keeping comment attachments alongside project photos on the same volume.【F:Services/ProjectCommentService.cs†L31-L48】【F:Services/Storage/IUploadRootProvider.cs†L1-L5】

### `Services/LoginAnalyticsService`
Calculates percentile lines and flags odd login events for the admin scatter chart. It loads `AuthEvents` from the database, joins them to user records to supply friendly names (falling back to email or "(deleted)"), applies working-hour rules and returns points annotated with reasons for anomalies.

### `Services/LoginAggregationWorker`
Nightly background service that aggregates the previous day's successful logins into `DailyLoginStats` to keep reporting queries fast.

### Notification services
* **`NotificationPublisher`** – normalises metadata, serialises payload envelopes, and writes `NotificationDispatch` rows while trimming overly long inputs and guarding against invalid project identifiers.【F:Services/Notifications/NotificationPublisher.cs†L16-L198】
* **`NotificationDispatcher`** – hosted worker that batches undispatched rows, honours per-kind preferences, deduplicates via fingerprints, persists `Notification` records, and pushes updates through the SignalR hub with exponential backoff on errors.【F:Services/Notifications/NotificationDispatcher.cs†L20-L200】
* **`NotificationRetentionService`** – deletes notifications and dispatches beyond the configured age or per-user cap to keep tables manageable.【F:Services/Notifications/NotificationRetentionService.cs†L20-L147】
* **`NotificationPreferenceService`** – centralises allow/deny checks for notification kinds, project mutes, and role-wide subscriptions so publishers can remain stateless.【F:Services/Notifications/NotificationPreferenceService.cs†L19-L160】
* **`RoleNotificationService`** – helper that resolves role memberships for broadcast notifications without duplicating Identity queries.【F:Services/Notifications/RoleNotificationService.cs†L14-L116】
* **`UserNotificationService`** – application-facing API that lists, counts, marks read/unread, and mutes notifications with project access guards to prevent leaking data across teams.【F:Services/Notifications/UserNotificationService.cs†L17-L220】

### Document workflow services
* **`DocumentService`** – core file pipeline that validates PDF uploads, enforces size/MIME rules, optionally scans for viruses, moves files between temp and permanent storage, records audits, and notifies stakeholders after publication.【F:Services/Documents/DocumentService.cs†L19-L420】
* **`DocumentRequestService`** – orchestrates request lifecycles (create, edit, submit, cancel) and persists temporary files before review.【F:Services/Documents/DocumentRequestService.cs†L12-L179】
* **`DocumentDecisionService`** – handles approvals or rejections, including publishing replacements, archiving old versions, emitting audit events, and issuing notifications.【F:Services/Documents/DocumentDecisionService.cs†L11-L188】
* **`DocumentNotificationService`** – resolves HoD/PO recipients, honours notification preferences, and pushes document activity into the notification pipeline with rich payloads and deduplicated fingerprints.【F:Services/Documents/DocumentNotificationService.cs†L15-L196】
* **`DocumentPreviewTokenService`** – issues short-lived tokens used to authorise inline PDF previews without exposing the underlying storage path.【F:Services/Documents/DocumentPreviewTokenService.cs†L10-L124】

### Logging
`appsettings.json` and `Program.cs` configure logging filters to keep output concise: verbose Entity Framework messages and routine To-Do service logs are suppressed, while the `TodoPurgeWorker` logs only warnings or higher. Additionally, `AuditService` skips writing `Todo.*` actions to the `AuditLogs` table.
