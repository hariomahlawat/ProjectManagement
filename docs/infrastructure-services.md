# Infrastructure and Services

This module documents cross-cutting infrastructure and service abstractions.

## Infrastructure

### `Infrastructure/EnforcePasswordChangeFilter.cs`
An `IAsyncPageFilter` applied globally. After authentication, it checks `ApplicationUser.MustChangePassword` and redirects to `/Identity/Account/Manage/ChangePassword` until the password is updated. Login, logout and the change-password page itself are exempt from the check.

## Services

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

### Email senders
* `Services/NoOpEmailSender.cs` – a dummy implementation used when SMTP settings are absent (common on private networks).
* `Services/SmtpEmailSender.cs` – sends HTML email via SMTP using configuration values (`Email:Smtp:Host`, `Port`, `Username`, `Password`, `Email:From`).

### `Services/ITodoService` and `TodoService`
`ITodoService` abstracts operations on personal To-Do items such as creation, completion, pinning, snoozing, reordering and bulk operations. `TodoService` implements the interface using `ApplicationDbContext` for persistence and `IAuditService` for logging. Time calculations are normalised to the `Asia/Kolkata` time zone to ensure consistent due date handling and `RowVersion` is used to detect concurrent edits.

### `Services/TodoPurgeWorker`
Background worker that permanently deletes soft-deleted to-do items after a retention period. The retention window is configured via `Todo:RetentionDays` in `appsettings.json` (defaults to 7 days) and the worker safely handles cancellation tokens.

### Logging
`appsettings.json` and `Program.cs` configure logging filters to keep output concise: verbose Entity Framework messages and routine To-Do service logs are suppressed, while the `TodoPurgeWorker` logs only warnings or higher. Additionally, `AuditService` skips writing `Todo.*` actions to the `AuditLogs` table.
