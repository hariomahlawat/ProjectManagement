# Development Environment

How to build, run and test PRISM locally. Every command below exists in this repository
(`package.json`, `tools/`, `.github/workflows/`, `ProjectManagement.csproj`).

## 1. Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | 8.0.x | `net8.0`. Restore the pinned local tool with `dotnet tool restore` (`dotnet-ef` 8.0.19, see `.config/dotnet-tools.json`). |
| Node.js + npm | 22 (the CI version) | **Required for every .NET build.** The `ValidateNotebookDependencies` / `BuildNotebookAssets` targets in `ProjectManagement.csproj` stop the build unless `node_modules/esbuild` exists. They rebuild `wwwroot/dist/notebook-*` when inputs change. |
| PostgreSQL | 16 (the CI version) | This is the only supported provider (Npgsql). |
| ripgrep (`rg`) | any | Used by `tools/check-views.sh`. |
| ocrmypdf | optional | Needed only to exercise OCR (see §3). |

Client libraries from `libman.json` are committed under `wwwroot/lib`, so no LibMan restore is needed.

## 2. Build and test

From the repository root:

```bash
npm ci
dotnet tool restore
dotnet restore
dotnet build
dotnet test
```

`dotnet restore` / `dotnet build` at the root pick up `ProjectManagement.sln`, which contains the web
project and `ProjectManagement.Tests/`. `ProjectManagement.csproj` declares
`RuntimeIdentifiers=win-x64`. Development and CI builds run fine on Linux.

### JavaScript and view checks

| Command | What it does |
| --- | --- |
| `npm test` | Runs `tools/run-js-tests.js`, which runs the `node --test` suites under `wwwroot/js/projects`, `wwwroot/js/notebook` and an explicit list of page tests. |
| `npm run build:notebook` (`build:notebook:prod` minifies) | Rebuilds `wwwroot/dist/notebook-index.bundle.js(.map)` and `notebook-manifest.json` with `tools/build-notebook.mjs`. |
| `npm run check:notebook-assets` | Rebuilds the Notebook bundle, then runs `git diff --quiet -- wwwroot/dist`. It fails if the committed bundle is stale. It **modifies** `wwwroot/dist` when the bundle is stale, so commit the result. |
| `./tools/check-views.sh` (also `npm run lint:views`) | Uses `rg` to scan `Areas`, `Pages`, `Views` and `wwwroot` for inline `<script>` blocks, inline event handlers and `style="` attributes. |

### PostgreSQL migration integration test

`ProjectManagement.Tests/PostgresMigrationIntegrationTests.cs` applies the full migration chain to a
real database. It is skipped unless both of these are set:

```bash
export PRISM_RUN_POSTGRES_MIGRATION_TESTS=true
export PRISM_TEST_POSTGRES_CONNECTION="Host=localhost;Port=5432;Database=prism_test_migrations;Username=prism;Password=...;Include Error Detail=true"
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj --filter FullyQualifiedName~PostgresMigrationIntegrationTests
```

The target database must be **empty**. The test asserts that there are zero tables and never resets
data. Use a database named `prism_test_*`.

The migration metadata tests need no database:
`--filter "FullyQualifiedName~ApplicationDatabaseMigrationsTests|FullyQualifiedName~MediaLibraryMigrationMetadataTests"`.

### Adding a migration

Do not run `dotnet ef migrations add` directly. Use the helper script, which keeps IDs in order and
updates the immutable manifest:

```powershell
./tools/Add-PrismMigration.ps1 -Name DescribeTheChange -Context ApplicationDbContext   # or MediaLibraryDbContext
```

See `MIGRATIONS-POLICY.md`. Both manifests (`Migrations/immutable-migration-ids.txt` and
`Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt`) must match the compiled
migrations, or startup stops.

## 3. Running locally

```bash
dotnet run --launch-profile ProjectManagement   # https://localhost:7183, http://localhost:7130
```

`Properties/launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`.

- **Database:** `appsettings.Development.json` connects to `Host=localhost;Database=ProjectManagement;Username=postgres;Password=postgres`.
  Override it with user secrets (the project has a `UserSecretsId`) or with `ConnectionStrings__DefaultConnection`.
  All pending migrations are applied automatically at startup (see `docs/deployment/offline-ws2022.md` §5).
- **OCR path:** `appsettings.Development.json` sets `ProjectDocuments:Ocr:OcrExecutablePath` to
  `C:/Python311/Scripts/ocrmypdf.exe`. `ProjectDocumentOcrOptionsValidator` runs on start and **stops the
  app if that file does not exist**. Point it to your local `ocrmypdf` with
  `dotnet user-secrets set "ProjectDocuments:Ocr:OcrExecutablePath" "<path>"`, and do the same for
  `DocRepo:OcrExecutablePath`.
- **Data paths:** the Development file uses `D:/ProjectManagementData/...` roots. On Linux or macOS, override
  `ProjectPhotos:StorageRoot` (or set `PM_UPLOAD_ROOT`), `DocRepo:RootPath`, `ProjectDocuments:Ocr:WorkRoot`,
  `MediaLibrary:CacheRoot` and `MediaLibrary:People:ModelRoot`. Face recognition (`MediaLibrary:People:Enabled`)
  is on in Development and loads models from `ModelRoot`. The models are in `App_Data/media-models`.
- **Data-protection keys:** in Development, keys go to `%LOCALAPPDATA%/PRISM-ERP/DataProtectionKeys` unless `DP_KEYS_DIR` is set.

### Seeding

Seeding is off by default (`Database:RunSeedersOnStartup=false` in every appsettings file). To seed an
empty database, set `Database:RunSeedersOnStartup=true` and provide the bootstrap admin password:

```bash
dotnet user-secrets set "Database:RunSeedersOnStartup" "true"
dotnet user-secrets set "Security:BootstrapAdminPassword" "<strong password>"   # or env PRISM_BOOTSTRAP_ADMIN_PASSWORD
# optional Development-only test users:
dotnet user-secrets set "DevelopmentSeedUsers:TestHoD:Password" "<pw>"             # user test_hod (HoD)
dotnet user-secrets set "DevelopmentSeedUsers:TestProjectOfficer:Password" "<pw>"  # user test_project_offr
```

This runs `IsoCountrySeeder`, `StageFlowSeeder` and `IdentitySeeder` (`Data/`). Accounts are created
with `MustChangePassword`. The admin user name comes from `Security:BootstrapAdminUserName` (default `admin`).

Other startup switches: `--compendium-offline-self-test` (checks fonts, SkiaSharp, QuestPDF and PdfPig
without a database, then exits) and `--backfill-forecast` (backfills stage forecast dates after migrations, then exits).

## 4. CI workflows (`.github/workflows/`)

| Workflow | Trigger | Steps |
| --- | --- | --- |
| `notebook.yml` (*Notebook Build and Tests*) | push / PR to `master` | `npm ci`, `npm test`, `npm run check:notebook-assets`, `dotnet restore`, `dotnet build`, `dotnet test` (whole suite, Debug). |
| `database-migrations.yml` (*Production Database Migration Gate*) | every push and PR | Starts a `postgres:16` service, runs `npm ci`, `dotnet build -c Release`, the migration metadata tests, then `PostgresMigrationIntegrationTests` with `PRISM_RUN_POSTGRES_MIGRATION_TESTS=true`. |
| `lint.yml` (*Lint Views*) | push to `main`, and all PRs | `./tools/check-views.sh`. The default branch is `master`, so the push trigger never fires. |
| `build-project-tot-v3-zip.yml` | pushes touching `ReadyToReplace/Project-Tot-Precision-and-UX-v3/**`, or manual | Obsolete. `ReadyToReplace/` no longer exists, so a manual run fails. |

Current state (checked locally): `npm test` reports 21 failing tests out of 810, and
`./tools/check-views.sh` exits 1 on inline `style="` attributes (57 in `.cshtml` files). Both break the corresponding workflows
until they are fixed.
