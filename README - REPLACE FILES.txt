PRISM ERP - Production Readiness and Migration Recovery v4.1
================================================================

BASELINE
--------
This is a cumulative ready-to-replace package for:

    ProjectManagement-aarav (1).zip

It includes the earlier Production Schema Recovery, migration-lineage bridges,
EventEndpoint test correction, and the final v4.1 production hardening. Earlier
patch packages do not need to be applied separately.

REPLACEMENT
-----------
Copy every enclosed project file to the same relative path in the
ProjectManagement source tree and replace the existing file. No project files are
deleted by this package.

WHAT IS FIXED
-------------
1. ApplicationDbContext and MediaLibraryDbContext are one mandatory startup
   migration boundary.
2. One dedicated non-pooled PostgreSQL advisory-lock session is held across both
   contexts and all critical physical-schema validation.
3. Both migration assemblies, immutable manifests and both database histories are
   preflighted before either context is changed.
4. Every pending migration is applied automatically before hosted workers or HTTP
   requests begin.
5. Unknown database-history IDs, incomplete publishes, duplicate IDs, remaining
   pending migrations or physical-schema drift cause fail-fast startup.
6. The obsolete ProjectStages constraint is repaired and can no longer be recreated
   by normal startup code. ActualStart remains optional for a completed stage.
7. Historical migration identities found in deployed databases are restored, and
   previously EF-invisible migration classes are now explicit immutable lineage
   bridges.
8. Media Library runtime/admin migration paths are removed. Media workers require a
   fully current schema.
9. Media Intelligence degrades safely if catalogue telemetry later becomes
   unavailable and avoids People queries when that capability is disabled.
10. Direct stage completion is atomic and reports schema, concurrency, validation
    and database failures distinctly.
11. Audit rows are never deleted during an application recycle. Optional retention
    is disabled by default and runs only as a bounded, configured worker.
12. Production secrets are not stored in appsettings files. Data-protection keys are
    persisted outside the publish directory and permission-tested before startup.
13. Migration metadata tests, isolated WebApplicationFactory tests and a guarded
    PostgreSQL 16 full-chain integration test are included.

MIGRATIONS ON STARTUP - CONFIRMED
---------------------------------
Yes. The application now applies all pending migrations for both EF Core contexts
on every first startup after deployment. Startup continues only after migration
closure and physical-schema validation succeed.

The setting Database:ApplyMigrationsOnStartup=false is deprecated and ignored.
This is intentional: a new application binary is not allowed to serve traffic with
an older schema.

REQUIRED PRODUCTION SETTINGS
----------------------------
Configure these outside source control through IIS application-pool/web.config
environment variables or the approved secret-management mechanism:

    ConnectionStrings__DefaultConnection=Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=...;
    DP_KEYS_DIR=D:\PRISM\DataProtectionKeys

DP_KEYS_DIR is mandatory outside Development, must be absolute and durable, and the
application-pool identity requires read/write/create/delete permission. The folder
is write-tested before startup. On Windows, persisted keys use machine-level DPAPI.

The PostgreSQL identity must be permitted to apply all committed migrations. Use a
dedicated PRISM database-owner/deployment role rather than the postgres superuser.

MANDATORY PRE-DEPLOYMENT CHECK
------------------------------
1. Back up the exact PostgreSQL database and existing IIS site directory.
2. Run PRODUCTION-MIGRATION-INVENTORY.sql against the database used by IIS.
3. Compare the complete histories with:
       Migrations/immutable-migration-ids.txt
       Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt
4. Do not delete or edit rows in either EF migration-history table.
5. Commit and push every migration, manifest and source file before publishing.

BUILD AND TEST
--------------
Run from the project root:

    npm ci
    dotnet tool restore
    dotnet clean
    dotnet restore
    dotnet build -c Release
    dotnet test -c Release
    dotnet publish -c Release -o .\publish

The repository CI workflow runs the complete Application and Media migration chains
against an empty PostgreSQL 16 database. For a local equivalent, follow
README-PRODUCTION-READINESS-V4.md and use a dedicated empty database whose name
starts with prism_test_.

DEPLOYMENT
----------
1. Place app_offline.htm in the IIS site root or fully stop the application pool.
2. Deploy the complete contents of the new Release publish directory. Do not copy
   .cs files into the server publish folder and do not replace only the DLL.
3. Confirm ConnectionStrings__DefaultConnection and DP_KEYS_DIR are available to the
   application-pool process.
4. Remove app_offline.htm/start the application pool.
5. Review logs. Traffic should be admitted only after both contexts report migration
   and validation completion.
6. Re-run PRODUCTION-MIGRATION-INVENTORY.sql.
7. Verify Photos, Media Intelligence and direct TEC stage completion with blank
   optional ActualStart.

FAILURE POLICY
--------------
A startup failure is protective. If an unknown migration ID is reported, do not
remove the database-history row and do not weaken DatabaseStartupMigrator. Compare
all history IDs with the immutable manifests and restore the exact historical
migration identity from the build that originally applied it.

VALIDATION PERFORMED IN THIS ENVIRONMENT
----------------------------------------
- All 65 changed/new files were compared against the supplied baseline.
- All changed C# files passed syntax parsing.
- JSON, project XML and GitHub Actions YAML parsed successfully.
- Application manifest: 60 unique ordered migration IDs.
- Media manifest: 10 unique ordered migration IDs.
- Manifest IDs exactly match migration metadata in source.
- Only DatabaseStartupMigrator can call Database.MigrateAsync.
- No HTTP migration endpoint remains.
- No audit-log deletion remains in normal startup.
- Production connection strings are blank in committed JSON.

A .NET SDK is not installed in this execution environment, so the final Release
compile and PostgreSQL integration test must be run using the commands above before
production deployment.
