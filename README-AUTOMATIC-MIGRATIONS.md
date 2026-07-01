# PRISM ERP — Mandatory Automatic Migrations

## Scope

PRISM treats `ApplicationDbContext` and `MediaLibraryDbContext` as one production
deployment boundary. Every deployed EF Core migration is applied and validated before the
application starts hosted workers or accepts HTTP requests.

History tables:

- `ApplicationDbContext` → `__EFMigrationsHistory`
- `MediaLibraryDbContext` → `__EFMigrationsHistory_MediaLibrary`

## Startup sequence

The application:

1. opens one PostgreSQL session and acquires the `PRISM_ERP_EF_MIGRATIONS` advisory lock;
2. confirms both contexts target the same PostgreSQL server and database;
3. verifies both immutable manifests, both EF migration assemblies and both database-history lineages before changing either context;
4. rejects unknown database-history IDs, missing bridge migrations or duplicate assembly IDs;
5. applies all pending Application migrations;
6. validates project-stage, Notebook, DocRepo favourites and project-document search schema;
7. applies all pending Media Library migrations;
8. validates the latest media migration, required tables/columns and representative queries;
9. confirms migration closure for both contexts; and
10. releases the lock only after the complete deployment boundary succeeds.

`Database:ApplyMigrationsOnStartup=false` is deprecated and ignored. Production migrations
are mandatory.

## Failure policy

Any migration-lineage mismatch, DDL failure, remaining pending migration or physical-schema
validation failure aborts startup. This is intentional. Do not delete migration-history rows
or weaken the gate to make the application start.

## Runtime mutation is prohibited

No Razor Page, hosted worker or administration action applies migrations after startup. The
Media Sources page is diagnostic only. Schema changes belong exclusively to checked-in EF
Core migrations and a complete Release deployment.

## Production permissions

The configured PostgreSQL identity must be permitted to run the DDL/DML contained in the
migrations and call PostgreSQL advisory-lock functions. Use a controlled deployment identity
consistent with organisational database policy.

## Historical migration lineage

Historical identifiers already recorded in shared databases are immutable. The repository
contains explicit no-op lineage bridges when the original DDL has been superseded by later
idempotent repair migrations. Do not rename or remove those bridge classes.

The authoritative lineage manifests are:

- `Migrations/immutable-migration-ids.txt`
- `Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt`

## Build and deployment

Before publishing:

```powershell
npm ci
dotnet tool restore
dotnet clean
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Run the PostgreSQL migration integration test or require the `Production Database Migration
Gate` CI workflow to pass. Then deploy the complete Release publish while the IIS application
is offline.

See:

- `README-PRODUCTION-READINESS-V4.md`
- `MIGRATIONS-POLICY.md`
- `PRODUCTION-MIGRATION-INVENTORY.sql`

## Publish integrity manifests

The Release output must contain both immutable lineage manifests at their original relative
paths. Startup compares EF Core discovery with these manifests before reading database
history. A partial publish, missing bridge migration or stale manifest causes fail-fast
startup rather than an uncertain schema upgrade.

## Credentials and key persistence

Production connection strings are supplied through `ConnectionStrings__DefaultConnection`;
they are not committed to production JSON. Set `DP_KEYS_DIR` to a durable absolute directory outside
the publish folder and grant the IIS application-pool identity read/write/create/delete permission.
Outside Development this setting is mandatory and is write-tested before startup continues.
