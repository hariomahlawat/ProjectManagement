# PRISM Production Readiness v4.1

## Purpose

This release closes the migration-lineage, startup coordination and runtime schema-mutation
issues found during the production Photos and project-stage incidents.

## Startup contract

`ApplicationDbContext` and `MediaLibraryDbContext` are migrated as one deployment boundary.
The process:

1. acquires one dedicated, non-pooled PostgreSQL advisory-lock session;
2. verifies both immutable manifests, both migration assemblies and both database histories before applying either context;
3. applies all pending Application migrations;
4. validates critical Application physical schema;
5. applies all pending Media Library migrations;
6. validates Media Library physical schema and representative queries;
7. verifies migration closure again, releases the deployment lock; and
8. only then starts workers and accepts HTTP requests.

A failure is fatal by design. PRISM will not run against a partially upgraded or downgraded
database.

## Important corrections

- Restores all historical migration IDs discovered in deployed databases.
- Restores metadata for three migration classes that previously compiled but were invisible
  to EF Core.
- Converts obsolete historical DDL into safe no-op lineage bridges; later idempotent
  migrations remain responsible for the final schema.
- Removes the second Media Library runtime migration pathway.
- Makes Media readiness derive the latest migration from the deployed assembly.
- Holds one advisory lock across both contexts and all critical physical checks.
- Preflights both lineages before any migration is applied, preventing partial upgrade when either history belongs to a different build.
- Removes audit-log deletion from ordinary startup.
- Adds an explicit, disabled-by-default, bounded audit-retention worker.
- Uses the active PostgreSQL schema instead of assuming `public` in validators.
- Replaces invalid whole-chain SQLite migration tests with metadata tests and a real,
  guarded PostgreSQL integration test.
- Adds immutable migration manifests and a migration-generation helper that preserves
  monotonic migration ordering.

## Build and test

From the project root:

```powershell
npm ci
dotnet tool restore
dotnet clean
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

The PostgreSQL chain test is skipped unless explicitly enabled. CI runs it against a fresh
PostgreSQL 16 database. To run it locally, use a dedicated empty database whose name begins
with `prism_test_`:

```powershell
$env:PRISM_RUN_POSTGRES_MIGRATION_TESTS = 'true'
$env:PRISM_TEST_POSTGRES_CONNECTION = 'Host=localhost;Port=5432;Database=prism_test_migrations;Username=...;Password=...'
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --filter FullyQualifiedName~PostgresMigrationIntegrationTests
```

The test refuses to reset or use a non-empty database.

## Production deployment

1. Back up PostgreSQL and the current IIS application directory.
2. Run `PRODUCTION-MIGRATION-INVENTORY.sql` against the exact production database.
3. Compare both history lists with the immutable manifests in source.
4. Commit and push every migration and manifest file.
5. Create a complete Release publish.
6. Stop the application pool or place `app_offline.htm` in the site root.
7. Replace the complete published output. Do not deploy source `.cs` files individually.
8. Remove `app_offline.htm` and start the pool.
9. Review startup logs. Do not admit traffic until both migration sets report completion.
10. Re-run `PRODUCTION-MIGRATION-INVENTORY.sql` and exercise Photos, Media Intelligence and
    direct stage completion.

## Audit retention

`Audit:Retention:Enabled` defaults to `false`. Enabling deletion is a governance decision.
When enabled, retention runs after startup in bounded batches and refuses a period below 30
days. An IIS recycle never deletes audit records.

## Creating future migrations

Do not run `dotnet ef migrations add` directly. Existing migration IDs extend beyond the
current calendar date. Use:

```powershell
./tools/Add-PrismMigration.ps1 -Name DescribeTheChange -Context ApplicationDbContext
```

or:

```powershell
./tools/Add-PrismMigration.ps1 -Name DescribeTheChange -Context MediaLibraryDbContext
```

See `MIGRATIONS-POLICY.md`.

## Required production environment settings

Production connection credentials are intentionally not stored in `appsettings.json` or
`appsettings.Production.json`. Configure the IIS application pool or `web.config`
environment variables before starting the site:

```text
ConnectionStrings__DefaultConnection=Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=...;
DP_KEYS_DIR=D:\PRISM\DataProtectionKeys
```

Grant the application-pool identity read/write/create/delete access to `DP_KEYS_DIR`. The path must be absolute and is write-tested before startup. On Windows, newly
persisted data-protection keys are encrypted with machine-level DPAPI. Use a dedicated
PostgreSQL database-owner account rather than the `postgres` superuser. The account must
be able to apply the committed EF migrations during startup.

The production host suppresses Npgsql detailed server errors even if a connection string
attempts to enable them.

## Test-host isolation

All `WebApplicationFactory` tests replace both EF Core contexts with isolated in-memory
databases and remove Media Library hosted workers. This prevents unit/API tests from
connecting to a developer or production PostgreSQL database now that both contexts form
one mandatory startup boundary.
