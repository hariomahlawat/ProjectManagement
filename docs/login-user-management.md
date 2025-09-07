# Login and User Management Module

This document describes the implementation of authentication and user management in the ProjectManagement application.  It is intended for developers who need to understand or extend the login flow, role management, and related services.

## High level architecture

The module is built on ASP.NET Core Identity and Razor Pages.  Identity is configured in `Program.cs` and backed by an Entity Framework Core database context.  User management operations are exposed through a dedicated service layer (`Services/UserManagementService.cs`), while UI interactions live under `Areas/Identity`.

```
Areas/Identity/Pages/Account      Razor pages for login, logout and password change
Data                              Database context and seeding
Infrastructure                    Cross‑cutting filters
Models                            Identity user model
Services                          User management and email helpers
Program.cs                        Application bootstrap and Identity configuration
```

## Bootstrapping and configuration

### `Program.cs`
* Configures the `ApplicationDbContext` connection string and enables the PostgreSQL provider.
* Registers ASP.NET Core Identity with relaxed password rules and a username/password flow only (`AddIdentity<ApplicationUser, IdentityRole>`).
* Customises the application cookie paths and enables session state.
* Registers `IUserManagementService` (`UserManagementService` implementation) and an email sender (`SmtpEmailSender` when SMTP settings are present or `NoOpEmailSender` otherwise).
* Adds the `EnforcePasswordChangeFilter` globally to Razor Pages to force password updates for flagged users.
* Seeds default roles and a first administrator account at application start through `IdentitySeeder.SeedAsync`.

### `appsettings.json`
Holds database and optional SMTP settings.  Developers can override the `Email:Smtp:*` keys to enable real email delivery.

## Data layer

### `Data/ApplicationDbContext.cs`
Derives from `IdentityDbContext<ApplicationUser>` and exposes the `Projects` table.  Identity tables (users, roles, claims, etc.) are provided by the base class.

### `Data/DesignTimeDbContextFactory.cs`
Provides a design‑time factory so Entity Framework tooling can create the context when running migrations.  It reads configuration from `appsettings.json`, `appsettings.Development.json`, or environment variables.

### `Data/IdentitySeeder.cs`
Seeds initial roles (`Admin`, `HoD`, `TeamLead`, `User`) and creates a default `admin` account with the password `ChangeMe!123` if it does not already exist.  The `admin` user has `MustChangePassword` set to `false` so it can log in immediately.

## Domain model

### `Models/ApplicationUser.cs`
Extends `IdentityUser` with a `MustChangePassword` flag.  New accounts are created with the flag set to `true`, forcing a password change on first login via `EnforcePasswordChangeFilter`.

## Infrastructure

### `Infrastructure/EnforcePasswordChangeFilter.cs`
An `IAsyncPageFilter` applied globally.  After authentication, it checks `ApplicationUser.MustChangePassword` and redirects to `/Identity/Account/Manage/ChangePassword` until the password is updated.  Login, logout and the change‑password page itself are exempt from the check.

## Services

### `Services/IUserManagementService.cs`
Defines an abstraction for managing users and roles:
* Query users and roles
* Create users with an initial role
* Update a user's role
* Toggle activation/lockout
* Reset passwords (marking the user for a forced change)
* Delete users

### `Services/UserManagementService.cs`
Concrete implementation backed by `UserManager<ApplicationUser>` and `RoleManager<IdentityRole>`.  It encapsulates all identity operations used by the administration UI or other features.  Developers can extend this service to add new user‑related behaviours such as emailing users after role changes or integrating external identity providers.

### Email senders
* `Services/NoOpEmailSender.cs` – a dummy implementation used when SMTP settings are absent (common on private networks).
* `Services/SmtpEmailSender.cs` – sends HTML email via SMTP using configuration values (`Email:Smtp:Host`, `Port`, `Username`, `Password`, `Email:From`).

## Razor Pages

### Login
* `Areas/Identity/Pages/Account/Login.cshtml` – form that collects a username and password; uses built‑in tag helpers for validation.
* `Areas/Identity/Pages/Account/Login.cshtml.cs` – authenticates the user with `SignInManager`, handles disabled accounts and lockouts, and records events via `ILogger`.

### Logout
* `Areas/Identity/Pages/Account/Logout.cshtml` – simple confirmation page after logout.
* `Areas/Identity/Pages/Account/Logout.cshtml.cs` – signs the user out and clears the session.

### Change Password
* `Areas/Identity/Pages/Account/Manage/ChangePassword.cshtml` – form for updating the current user's password.
* `Areas/Identity/Pages/Account/Manage/ChangePassword.cshtml.cs` – validates the old password, updates it, clears the `MustChangePassword` flag and refreshes the sign‑in cookie.

## Extending the module

* **Adding new roles** – update `IdentitySeeder` and run the seeder again or create migration scripts.
* **Custom user properties** – extend `ApplicationUser` and update any forms or view models accordingly.
* **Alternative email providers** – implement `IEmailSender` and register it in `Program.cs`.
* **Additional account pages** – place new Razor Pages under `Areas/Identity/Pages/Account` and secure them with the existing Identity configuration.

This overview should give developers enough context to modify or extend the login and user management features safely.
