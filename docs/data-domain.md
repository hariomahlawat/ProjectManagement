# Data and Domain

This module covers the persistence layer and core domain types.

## Data layer

### `Data/ApplicationDbContext.cs`
Derives from `IdentityDbContext<ApplicationUser>` and exposes the `Projects` table. Identity tables (users, roles, claims, etc.) are provided by the base class.

### `Data/DesignTimeDbContextFactory.cs`
Provides a design-time factory so Entity Framework tooling can create the context when running migrations. It reads configuration from `appsettings.json`, `appsettings.Development.json`, or environment variables.

### `Data/IdentitySeeder.cs`
Seeds initial roles (`Admin`, `HoD`, `TeamLead`, `User`) and creates a default `admin` account with the password `ChangeMe!123` if it does not already exist. The `admin` user has `MustChangePassword` set to `false` so it can log in immediately.

## Domain model

### `Models/ApplicationUser.cs`
Extends `IdentityUser` with a `MustChangePassword` flag. New accounts are created with the flag set to `true`, forcing a password change on first login via `EnforcePasswordChangeFilter`.
