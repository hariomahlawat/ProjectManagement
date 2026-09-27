# Offline Deployment on Windows Server 2022 (IIS)

This is the deployment standard for **PRISM** (`ProjectManagement`) on Windows Server 2022 with IIS,
in an air-gapped or LAN-only network. It describes the current build. Where this page and the code
disagree, the code is authoritative. Key sources: `ProjectManagement.csproj`, `web.config`,
`Program.cs`, `Infrastructure/DatabaseStartupMigrator.cs`, `Infrastructure/StartupFailureReporter.cs`,
`ops/publish/create-publish-folder.ps1`.

## 1. Prerequisites

### Build machine (internet-connected)

| Item | Requirement | Why |
| --- | --- | --- |
| .NET SDK | 8.0.x | `TargetFramework` is `net8.0`. The local tool `dotnet-ef` 8.0.19 is pinned in `.config/dotnet-tools.json`. |
| Node.js + npm | Node 22 is the version CI uses | The `BuildNotebookAssets` target in `ProjectManagement.csproj` runs `npm run build:notebook` (esbuild) before every `Build`/`Publish`. The build stops with *"Notebook frontend dependencies are missing. Run 'npm ci'..."* when `node_modules/esbuild` is absent. |
| LibMan | Not needed | The files listed in `libman.json` are already committed under `wwwroot/lib`. |

Node is used only at build time. The server does not need Node.

### Server

| Item | Requirement |
| --- | --- |
| IIS | Web Server role. Add the **.NET 8 Hosting Bundle** (ASP.NET Core Module V2). `web.config` uses `modules="AspNetCoreModuleV2"` and `hostingModel="inprocess"`, so the module is needed even for a self-contained publish. If you install IIS after the Hosting Bundle, repair the Hosting Bundle. |
| HTTPS certificate | **Required.** Outside Development, the auth and antiforgery cookies are `__Host-PMAuth` / `__Host-PMAntiforgery` with `CookieSecurePolicy.Always`. The app also calls `UseHttpsRedirection()` and `UseHsts()`. Login does not work over plain HTTP. |
| PostgreSQL | 16.x. CI runs `postgres:16`, and the ops scripts default to `C:\Program Files\PostgreSQL\16\bin`. The migrations run `CREATE EXTENSION IF NOT EXISTS pg_trgm` and `pgcrypto`. On PostgreSQL 13+ both are trusted extensions, so a database-owner role can create them. The media-library features do **not** need pgvector (`ops/media-library/verify-pgvector.sql`). |
| OCR | `ocrmypdf` and its runtime dependencies (Tesseract, Ghostscript). The app runs it through `Services/Ocr/OcrmypdfSharedRunner.cs` with `--skip-text` / `--force-ocr` / `--redo-ocr --sidecar`. Configure the path with `ProjectDocuments:Ocr:OcrExecutablePath` and `DocRepo:OcrExecutablePath`. If a path is empty, the app runs `ocrmypdf` from `PATH`. If `ProjectDocuments:Ocr:OcrExecutablePath` is set but the file does not exist, **startup fails** (`ProjectDocumentOcrOptionsValidator` with `ValidateOnStart`). The committed `appsettings.Production.json` points to `C:/Python311/Scripts/ocrmypdf.exe`. |
| PDF fonts | Nothing to install. The DM Sans and Alatsi publication fonts are committed under `wwwroot/fonts/publications/` and published with the app. SkiaSharp native assets for Windows come from NuGet (`SkiaSharp.NativeAssets.Win32`). |
| Face-recognition ONNX models | Needed only when `MediaLibrary:People:Enabled=true`. The approved models are committed in `App_Data/media-models/` (`face_detection_yunet_2026may.onnx`, `face_recognition_sface_2021dec.onnx`). The build does **not** publish `.onnx` files, so copy them by hand to `MediaLibrary:People:ModelRoot`. The file name and SHA-256 must match `MediaLibrary:People:Detector/Embedder` in configuration. |

## 2. Publish

Run on the build machine, from the repository root:

```powershell
npm ci
dotnet tool restore
dotnet restore
dotnet test -c Release        # optional here; CI also runs it
.\ops\publish\create-publish-folder.ps1
```

`create-publish-folder.ps1` does the following:

- runs `npm ci --ignore-scripts` if esbuild is missing
- checks every `appsettings*.json`
- runs `dotnet publish -c Release --runtime win-x64 --self-contained true /p:UseAppHost=true` into `artifacts/publish/ProjectManagement`
- checks the required published files, the self-contained runtime DLLs, the DM Sans fonts, `libSkiaSharp.dll` and the `web.config` request limit
- runs `ProjectManagement.exe --compendium-offline-self-test`

> **Known defect:** the script requires exactly **62** IDs in `Migrations/immutable-migration-ids.txt`
> ending in `20261201160000_FinalizeProjectStageCompletionConstraint`. The manifest now has 115 IDs,
> so the script always stops at that check until the check is updated.
> Until then, publish by hand with the same settings:

```powershell
dotnet publish .\ProjectManagement.csproj -c Release -r win-x64 --self-contained true /p:UseAppHost=true -o .\artifacts\publish\ProjectManagement
.\ops\publish\test-compendium-offline-payload.ps1 -PublishRoot .\artifacts\publish\ProjectManagement
```

`ops/publish/create-publish-folder.sh` produces a framework-dependent build (`UseAppHost=false`,
output `./publish`) with no `ProjectManagement.exe`. That build does not match `web.config` and is
not the IIS artifact.

The publish output must contain `Migrations/immutable-migration-ids.txt` and
`Features/MediaLibrary/Data/Migrations/immutable-migration-ids.txt`. They are copied by the csproj
and are also embedded in the DLL as a fallback.

## 3. Configuration

Configuration comes from `appsettings.json`, then `appsettings.Production.json`, then environment
variables (`Section__Key`). The committed `appsettings.Production.json` contains
`Username=postgres;Password=postgres` and `F:/ProjectManagementData/...` paths. Override them for your server.

| Setting | Purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Leave unset or set to `Production`. `web.config` sets no environment variables. |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection. Use a dedicated database-owner role. A warning is logged when the user is `postgres` outside Development. |
| `DP_KEYS_DIR` | Data-protection key ring. **Set it explicitly** to a durable folder and back it up. If it is unset outside Development, the app uses `/var/pm/keys` (on Windows, `\var\pm\keys` on the current drive). Losing the keys signs everyone out and invalidates antiforgery tokens. |
| `PM_UPLOAD_ROOT` or `ProjectPhotos:StorageRoot` | Upload root (`Services/Storage/UploadRootProvider.cs`). The environment variable wins. The default is `/var/pm/uploads`. |
| `DocRepo:RootPath` | Document repository files. A relative path is resolved against the content root. DocRepo OCR scratch space is `DocRepo:OcrWorkRoot`, which is resolved under the upload root when relative. |
| `ProjectDocuments:Ocr:WorkRoot` | Project-document OCR working folder. |
| `MediaLibrary:CacheRoot`, `MediaLibrary:People:ModelRoot` | Media derivative cache and ONNX model folder. A relative path is resolved against the content root. |
| `Storage:DataRoot` | Used **only** by `StartupFailureReporter`, which writes `startup-diagnostics/startup-failure-*.log` there, or under the content root if the value is missing or not absolute. No other root is derived from it. |
| `PRISM_COMPENDIUM_DIAGNOSTICS_DIR` | Optional durable folder for JSONL records of failed Compendium PDF generation. |
| `PRISM_PUBLICATION_FONTS_DIR` | Optional external publication-font root that contains `dm-sans/`. Set it only if the fonts are kept outside the site. |
| `Database:RunSeedersOnStartup` | Default `false`. Set it to `true` for the **first start on an empty database** only (see §5). |
| `PRISM_BOOTSTRAP_ADMIN_PASSWORD` or `Security:BootstrapAdminPassword` | One-time password for the bootstrap admin (user name from `Security:BootstrapAdminUserName`, default `admin`). It is required when seeders run and that user does not exist. The account is created with `MustChangePassword`. Remove the secret afterwards. |

`Database:ApplyMigrationsOnStartup` is ignored. Migrations always run, and a warning is logged if it is `false`.

Keep every data folder outside the site folder. Grant the app-pool identity Modify rights on each
data root and on `DP_KEYS_DIR`.

## 4. IIS site

1. Copy the publish output to, for example, `D:\Sites\PRISM\current`.
2. Create an app pool with **No Managed Code**. The app runs in process in `w3wp.exe`.
3. Create the site, add an **HTTPS** binding with the server certificate, and open the firewall port.
4. Set environment variables for the site/app pool (for example with `appcmd` or Configuration Editor → `system.webServer/aspNetCore/environmentVariables`).
5. `web.config` limits request bodies to 256 MiB (`maxAllowedContentLength=268435456`). Keep this value when you edit `web.config`.
6. For troubleshooting, set `stdoutLogEnabled="true"` temporarily. Logs go to `.\logs\stdout*`, so the identity needs write access to `logs`.

## 5. First start and migrations

Startup (see `Program.cs`, the "Database startup policy" section, and `DatabaseStartupMigrator`) works as follows:

1. The app checks the EF migrations in the assembly against the immutable manifest. It stops if either
   list has an ID the other lacks, if IDs are duplicated, or if the database history has IDs this build does not know.
2. It takes the PostgreSQL advisory lock `PRISM_ERP_EF_MIGRATIONS` (waits up to 10 minutes) and applies all
   pending `ApplicationDbContext` migrations. The command timeout is 600 s.
3. It validates the application schema (stage constraint, Notebook, DocRepo favourites, document search vectors).
   **Any failure here ends the process**, and a diagnostic is written by `StartupFailureReporter`.
4. If `MediaLibrary:Enabled` is true (the default), it then migrates and validates `MediaLibraryDbContext`
   (history table `__EFMigrationsHistory_MediaLibrary`) with its own lock. **A media failure is not fatal.**
   A critical log and a diagnostic file are written, the core ERP starts, and Photos/media workers stay unavailable.
5. When `Database:RunSeedersOnStartup=true`, the ISO country, StageFlow and Identity seeders run.

For a fresh database: start once with `Database__RunSeedersOnStartup=true` and
`PRISM_BOOTSTRAP_ADMIN_PASSWORD`, log in as `admin`, change the password, then remove both settings
and recycle the pool.

The first start after an upgrade can take several minutes while migrations run. Take a database
backup before every upgrade (see `docs/disaster-recovery.md`).

## 6. Verification

1. On the server, in the deployed folder, run `.\ProjectManagement.exe --compendium-offline-self-test`.
   It runs before the web host is built (no database, no network) and prints one JSON line with
   `"status":"ok"`, or exits non-zero.
2. Start the site. In the logs, look for `Migration preflight passed for ApplicationDbContext`,
   `ApplicationDbContext migrations are closed and the critical application schema is validated.` and the
   `Using database ... media startup healthy=True` line.
3. If startup failed, read `<Storage:DataRoot>\startup-diagnostics\startup-failure-*.log`.
4. Run `PRODUCTION-STARTUP-DIAGNOSTIC.sql` / `PRODUCTION-MIGRATION-INVENTORY.sql` (both read-only) against the database to check migration history.
5. Log in over HTTPS and open **Admin → System health** (`/Admin/Diagnostics/DbHealth`). It checks the database,
   upload/DocRepo storage, the data-protection key folder, capacity and background services.
   The app has **no** anonymous `/health` or `/health/ready` endpoint.
6. Smoke test: upload a document and confirm OCR completes, run a global search, and generate a Compendium PDF.

## 7. Update and rollback

**Update**

1. Back up the database (`ops/backup-db.ps1`) and the current site folder.
2. Put `app_offline.htm` in the site folder, or stop the app pool.
3. Replace the **whole** publish output. Never copy individual source files.
4. Remove `app_offline.htm` and start the pool. Migrations run on first start.
5. Repeat the checks in §6.

**Rollback**

EF migrations only move forward, and the app refuses to start against a database whose history
contains migrations the older build does not know. To roll back across a migration boundary,
restore the pre-upgrade database dump **and** the previous publish folder together.
