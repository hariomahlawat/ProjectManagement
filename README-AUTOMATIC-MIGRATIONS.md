# PRISM ERP — Automatic Production Migration Hardening

## Purpose

This patch makes database upgrade execution a mandatory application-startup gate. After an updated Release build is deployed, the first application process applies every pending migration before ASP.NET Core begins accepting requests.

Both EF Core migration sets are covered:

1. `ApplicationDbContext` using the standard `__EFMigrationsHistory` table.
2. `MediaLibraryDbContext` using its isolated `__EFMigrationsHistory_MediaLibrary` table.

A failed or incomplete migration prevents the application from starting against an incompatible schema.

## Installation

Copy this package into the `ProjectManagement` application root and replace the matching files. Preserve the folder structure.

No manual SQL script and no manual `dotnet ef database update` command is required for normal production deployment.

## Production deployment procedure

1. Back up the PostgreSQL database and the current deployed application.
2. Stop the IIS application pool.
3. Build and publish the updated application in Release configuration.
4. Deploy the complete published output, including the `Migrations` assembly content.
5. Start the IIS application pool.
6. Review startup logs before allowing users into the system.

The application will:

- acquire a PostgreSQL advisory lock;
- wait safely if another IIS worker is already migrating;
- apply all pending main-application migrations;
- verify that every migration shipped in the deployed assembly is recorded as applied;
- validate critical project-stage, notebook, favourites, requested-start-date and search-vector schema elements;
- apply all pending Media Library migrations using its independent history table;
- validate the Media Library schema;
- start the HTTP pipeline only after all checks pass.

## Expected startup log sequence

Typical first start after an update:

```text
Acquired the PostgreSQL migration advisory lock.
Applying N pending EF Core migration(s) before application startup: ...
Automatic database migration completed successfully.
All N migrations in the deployed assembly are recorded as applied.
Database startup gate completed. Latest applied migration: ...
Acquired media-library migration advisory lock.
Applying N pending media-library migration(s). ...
Media-library migrations and schema validation completed successfully.
```

Typical later restart with the same build:

```text
Database schema is current; no pending EF Core migrations were found.
All N migrations in the deployed assembly are recorded as applied.
Media-library schema has no pending migrations.
```

## Failure policy

Migration and schema-validation failures are deliberately fatal during startup. The application does not serve requests with a stale or partially upgraded schema. Check the exception and PostgreSQL details in the application log, correct the underlying problem, and restart. EF Core will retry only migrations not recorded as applied.

## Database account permissions

The production connection identity must be allowed to execute the DDL and DML contained in migrations, including `CREATE`, `ALTER`, `DROP`, `CREATE INDEX`, and `UPDATE`. It must also be allowed to call PostgreSQL advisory-lock functions.

## Important implementation notes

- `Database:ApplyMigrationsOnStartup=false` is now ignored and logged as deprecated. Migrations are mandatory.
- `MediaLibrary:AutoMigrate=false` is retained only for configuration compatibility and does not disable the startup migration gate.
- Recurring schema-changing SQL has been removed from ordinary startup code.
- Project-document search functions, triggers, index creation and historical vector backfill are migration-owned and execute once.
- The previous optional Media Library background migrator is no longer registered, preventing duplicate migration attempts.
- The correction migration is idempotent for PostgreSQL production databases that received earlier startup repairs.

## Local release gate

Run before publishing:

```powershell
npm ci
npm test
dotnet clean
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

The packaging environment did not contain the .NET SDK, so the final C# Release build must be completed locally.
