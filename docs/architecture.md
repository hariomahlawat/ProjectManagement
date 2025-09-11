# Architecture and Configuration

This module outlines how authentication and user management are wired into the application.

## High level architecture

The feature set is built on ASP.NET Core Identity and Razor Pages. Identity is configured in `Program.cs` and backed by an Entity Framework Core database context. User management operations are exposed through a dedicated service layer while UI interactions live under `Areas/Identity`.

```
Areas/Identity/Pages/Account      Razor pages for login, logout and password change
Data                              Database context and seeding
Infrastructure                    Cross-cutting filters
Models                            Identity user model
Services                          User management and email helpers
Program.cs                        Application bootstrap and Identity configuration
```

## Bootstrapping and configuration

### `Program.cs`
* Configures the `ApplicationDbContext` connection string from configuration or environment variables and enables the PostgreSQL provider.
* Adds console logging and reports the target host and database at startup.
* In Development, logs the current database and PostgreSQL server version and enables rich provider errors via `Include Error Detail=true`.
* Asserts that migration `20250909153316_UseXminForTodoItem` has run; logs a warning if not.
* Registers ASP.NET Core Identity with relaxed password rules and a username/password flow only (`AddIdentity<ApplicationUser, IdentityRole>`).
* Customises the application cookie paths and enables session state.
* Registers `IUserManagementService` (`UserManagementService` implementation) and an email sender (`SmtpEmailSender` when SMTP settings are present or `NoOpEmailSender` otherwise).
* Registers `IAuditService` for structured audit logging.
* Configures `UserLifecycleOptions`, registers `IUserLifecycleService` and hosts `UserPurgeWorker` to remove accounts past their deletion undo window.
* Adds the `EnforcePasswordChangeFilter` globally to Razor Pages to force password updates for flagged users.
* Seeds default roles and a first administrator account at application start through `IdentitySeeder.SeedAsync`.

### `appsettings.json`
Holds database and optional SMTP settings. Developers can override the `Email:Smtp:*` keys to enable real email delivery.
