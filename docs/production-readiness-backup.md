# Production Readiness: Backup and Restore Plan

**Application:** PRISM (`ProjectManagement`, ASP.NET Core 8 + PostgreSQL)

This page lists what must be protected, where the application actually keeps it, and how to back
it up with the `ops/` scripts. The step-by-step restore procedure is in `docs/disaster-recovery.md`.

## 1. What to back up

1. **PostgreSQL database.** It holds all records, Identity, both migration histories
   (`__EFMigrationsHistory`, `__EFMigrationsHistory_MediaLibrary`) and search projections.
2. **File data roots** (listed in §2).
3. **Data-protection key ring** (`DP_KEYS_DIR`). Without it, authentication cookies and antiforgery
   tokens issued before the restore become invalid.
4. **Configuration:** `appsettings.Production.json`, the site/app-pool environment variables, `web.config`
   changes and the TLS certificate.
5. **The deployed publish folder** for the running release. The app refuses a database whose migration
   history contains IDs unknown to the build, so a restore needs a build that matches the database.

## 2. Where files live

There is **no single data root in code**. Each store resolves its own path. `Storage:DataRoot` is read
only by `Infrastructure/StartupFailureReporter.cs`, which writes startup diagnostics there. The
committed `appsettings.Production.json` puts every root under one folder (`F:/ProjectManagementData`).
Keep that convention so a single mirror covers everything.

| Store | Setting (production value in repo) | Resolution |
| --- | --- | --- |
| Uploads: project photos, documents, videos, visits, social media, IPR, ARPP and FFC attachments, activities | `PM_UPLOAD_ROOT`, else `ProjectPhotos:StorageRoot` (`F:/ProjectManagementData/uploads`), else `/var/pm/uploads` | `Services/Storage/UploadRootProvider.cs` |
| DocRepo files | `DocRepo:RootPath` (`F:/ProjectManagementData/DocRepo`) | Relative paths resolve against the content root (`Services/DocRepo/LocalDocStorageService.cs`). |
| DocRepo OCR scratch | `DocRepo:OcrWorkRoot` (`ocr-work`) | Relative paths resolve under the upload root. |
| Project-document OCR scratch | `ProjectDocuments:Ocr:WorkRoot` (`F:/ProjectManagementData/project-ocr`) | |
| Media derivative cache | `MediaLibrary:CacheRoot` (`F:/ProjectManagementData/media-cache`) | Can be regenerated, but slowly. |
| Face ONNX models | `MediaLibrary:People:ModelRoot` (`F:/ProjectManagementData/media-models`) | Source copies are in `App_Data/media-models/`. |
| Startup diagnostics | `Storage:DataRoot` (`F:/ProjectManagementData`) → `startup-diagnostics/` | |
| Data-protection keys | `DP_KEYS_DIR`, default `/var/pm/keys` outside Development | Usually **not** under the data root. Back it up separately, or point it inside the data root. |

Records store paths relative to their module root (for example, DocRepo stores
`Path.GetRelativePath(root, file)`), so a restore to a different drive only needs the settings changed.

## 3. Backup procedure

### Database (nightly)

Scheduled with Task Scheduler or cron:

- Linux: `ops/backup-db.sh`, configured with `PGBIN`, `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER` and `PM_BACKUP_DIR`.
- Windows: `ops/backup-db.ps1 -PgBin ... -DbName ... -User ... -BackupDir ...`.

Both produce a `pg_dump --format=custom` file named `<db>-yyyyMMdd-HHmmss.dump`.

- Supply the password with `PGPASSWORD` or a pgpass file. Use a role that can read every table
  (for example, a member of `pg_read_all_data` on PostgreSQL 14+).
- The PowerShell database scripts currently fail because their `$Host` parameter collides with PowerShell's
  automatic variable (see `docs/disaster-recovery.md` §6). Until that is fixed, schedule `pg_dump.exe` directly:
  `pg_dump.exe --format=custom --host=... --port=5432 --username=pm_backup --file=<path>.dump ProjectManagement`.
- Always pass the backup directory explicitly. The Linux default puts dumps **inside** the data root
  (`/srv/projectmanagement-data/backups/db`), so the next file mirror copies them again.

### Files (nightly)

- Linux: `ops/backup-files.sh` with `PM_DATA_ROOT` and `PM_FILE_BACKUP_ROOT`.
- Windows: `ops/backup-files.ps1 -DataRoot F:\ProjectManagementData -BackupRoot <other volume>`.
  The script's default `DataRoot` is `D:\ProjectManagementData`, which does not match the committed production config.

Each run writes a **full** copy into a new `Data-<timestamp>` folder. The scripts do not prune old
copies, so add a retention job (suggested: 7 daily and 4 weekly copies). Keep the backup root on a
different volume from the data root.

### Offline copy (weekly)

Copy the latest dump, the latest file snapshot, the key ring and the configuration to offline or offsite
media, encrypted at rest.

## 4. Restore (summary)

1. Provision the OS, IIS with the .NET 8 Hosting Bundle, PostgreSQL (same major version) and `ocrmypdf`.
2. Restore the file roots: `PM_FILE_BACKUP_SOURCE=<snapshot> ops/restore-files.sh`, or
   `ops/restore-files.ps1 -BackupSource <snapshot> -DataRoot <root>`. These mirror the snapshot and delete extra files.
3. Restore `DP_KEYS_DIR`.
4. Stop the application, then restore the database: `PM_DUMP_FILE=<dump> ops/restore-db.sh`, or
   `pg_restore` directly on Windows. The database is dropped and recreated.
5. Deploy the matching publish folder and configuration, then start the site. Startup applies any pending
   migrations and validates the schema.
6. Smoke test: log in, open projects, download an attachment, upload a file, and check
   **System health** (`/Admin/Diagnostics/DbHealth`).

## 5. Security

- Use dedicated PostgreSQL roles: a read-only role for backups, and a database-owner role (not `postgres`)
  for the application. The app logs a warning when it connects as `postgres` outside Development.
- Never commit credentials. The committed `appsettings*.json` files contain `Username=postgres;Password=postgres`
  placeholders. Override `ConnectionStrings__DefaultConnection` through environment variables on the server.
- Restrict the data roots and `DP_KEYS_DIR` to the app-pool identity and the backup account.

## 6. Not implemented

- Point-in-time recovery (WAL archiving or replication).
- Backup retention and monitoring or alerting for backup jobs.
- Built-in encryption of backup files.
