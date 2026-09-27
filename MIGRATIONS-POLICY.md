# PRISM EF Core Migration Policy

## Migration sets

| Context | Migrations folder | History table | Immutable manifest |
|---|---|---|---|
| `ApplicationDbContext` | `Migrations/` | `__EFMigrationsHistory` | `Migrations/immutable-migration-ids.txt` |
| `MediaLibraryDbContext` | `Features/MediaLibrary/Data/Migrations/` | `__EFMigrationsHistory_MediaLibrary` | `Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt` |

Both contexts use the same PostgreSQL database (`ConnectionStrings:DefaultConnection`).

## Startup gate (what the code does)

Startup applies every migration before the host serves requests or starts hosted services. The steps are in the "Database startup policy" section of `Program.cs`. The machinery is `Infrastructure/DatabaseStartupMigrator.ApplyDeploymentBoundaryAsync` and `Infrastructure/MigrationLineageManifest.LoadRequired`.

### Per migration set

1. **Load the manifest.** The publish-folder copy is used first, with the embedded resource as a fallback:
   - `ProjectManagement.Migrations.immutable-migration-ids.txt`
   - `ProjectManagement.MediaLibrary.Migrations.immutable-migration-ids.txt`

   The manifest must be non-empty, free of duplicates, in ordinal order and in `yyyyMMddHHmmss_Name` format. `#` comment lines are ignored.
2. **Take the advisory lock.** On a dedicated non-pooled connection, take the PostgreSQL session advisory lock `hashtextextended('PRISM_ERP_EF_MIGRATIONS', 0)`. The lock is polled every second, and startup fails after 10 minutes.
3. **Preflight.**
   - The assembly must contain migrations.
   - No IDs may be duplicated.
   - The discovered IDs must exactly equal the manifest, in order.
   - No applied migration may be unknown to the assembly. Downgrades are refused.
4. **Migrate.** Run `MigrateAsync` with a 600-second command timeout.
5. **Verify closure.** Every known migration must be recorded as applied.
6. **Validate the physical schema.**
   - Application:
     - `ApplicationDatabaseSchemaValidator` (the `ProjectStages` columns and the `CK_ProjectStages_CompletedHasDate` constraint)
     - the Notebook migration-order check
     - the DocRepo favourites schema check
     - `ProjectDocumentSearchVectorMaintenance.ValidateAsync`
   - Media: `IMediaLibrarySchemaService.GetStatusAsync().IsCurrent`.
7. **Re-check and release.** History is re-checked, then the lock is released. Closing the session also releases it.

### How the two sets differ

- **Separate calls.** Program.cs runs the two sets **sequentially, in separate `ApplyDeploymentBoundaryAsync` calls**. Each call takes the advisory lock itself. The joint multi-plan boundary exists in `DatabaseStartupMigrator`, and `PostgresMigrationIntegrationTests` exercises it, but production startup does not use it.
- **Order.** Application migrations are applied before the Media lineage is checked.
- **Failure handling.**
  - A failure in the **Application** set is fatal. The process writes `startup-diagnostics/startup-failure-*.log` via `StartupFailureReporter` and does not start.
  - A failure in the **Media** set is logged as Critical, with a diagnostic file, but the core ERP still starts. Photos and the media workers stay unavailable until the media schema is corrected.
  - The Media set is skipped entirely when `MediaLibrary:Enabled=false`.
- **The `ApplyMigrationsOnStartup` setting has no effect.** Migrations are mandatory. `Database:ApplyMigrationsOnStartup=false` is ignored and only logs a deprecation warning.

Runtime pages and hosted workers must never execute `Database.Migrate()`.

## Immutable identity

Once a migration identifier has been applied to any shared database, its complete ID is permanent. Do not rename, delete, reuse or edit an applied migration. Historical lineage bridges must remain in source even when later migrations supersede their original DDL.

The manifests are authoritative. They are copied to the publish output and also embedded in the assembly (`ProjectManagement.csproj`). The startup gate refuses to run if the manifest and the compiled migrations disagree.

## Creating a migration

Some existing migration timestamps are later than the current calendar date. A plain `dotnet ef migrations add` can therefore place new work in the middle of the chain. Use the helper from the project root:

```powershell
./tools/Add-PrismMigration.ps1 -Name DescribeTheChange -Context ApplicationDbContext
./tools/Add-PrismMigration.ps1 -Name DescribeTheChange -Context MediaLibraryDbContext
```

The helper:

1. Runs `dotnet tool restore` and `dotnet ef migrations add` for the chosen context.
2. If the generated timestamp is not later than the manifest tail, moves it to one second after the tail. This renames the `.cs` and `.Designer.cs` files and rewrites the ID inside them.
3. Adds the new ID to the manifest and re-sorts it.

Commit the migration, designer, model snapshot and manifest together.

## Verification

CI runs the `Production Database Migration Gate` workflow (`.github/workflows/database-migrations.yml`) on every push and pull request, against a PostgreSQL 16 service. It performs these steps:

1. `npm ci`
2. `dotnet restore`
3. `dotnet build -c Release --no-restore`
4. Metadata tests: `ApplicationDatabaseMigrationsTests` and `MediaLibraryMigrationMetadataTests`. These check that each manifest exactly matches EF Core discovery, that IDs are ordered, and that each migration has the right context attributes.
5. `PostgresMigrationIntegrationTests`, which migrates an empty `prism_test_*` database through both contexts and checks that preflight refuses unknown history. The test runs only when `PRISM_RUN_POSTGRES_MIGRATION_TESTS=true` and `PRISM_TEST_POSTGRES_CONNECTION` are set, and is skipped otherwise.

Before deploying locally:

1. `npm ci`
2. `dotnet tool restore`
3. `dotnet restore`
4. `dotnet build -c Release`
5. `dotnet test -c Release`. Set the two environment variables above to include the PostgreSQL chain test.
6. Compare production history using `PRODUCTION-MIGRATION-INVENTORY.sql`.
7. Deploy a complete Release publish, with both manifest files present. Never copy source files into the IIS publish folder.

Database downgrades are unsupported. Do not delete rows from either history table to get past the startup gate.

The repository-local `dotnet-ef` tool is pinned in `.config/dotnet-tools.json` to 8.0.19 with `rollForward: false`, matching the EF Core 8 packages. Do not upgrade it independently of the EF Core runtime and tooling packages.
