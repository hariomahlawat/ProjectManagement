# Disaster Recovery Runbook

This runbook covers restoring PRISM after hardware loss, OS corruption or accidental data deletion,
using the scripts in `ops/`. See `docs/production-readiness-backup.md` for what must be backed up,
and `docs/deployment/offline-ws2022.md` for deploying the application.

## 1. What you need

- **Software:** the .NET 8 Hosting Bundle (IIS), or nothing extra for a self-contained publish on Linux,
  plus PostgreSQL with the **same major version** as the dump (16.x in CI and the script defaults),
  plus `ocrmypdf` if OCR is used.
- **Build artifact:** the complete publish folder for the release that was running, or one built from the same commit.
  The app refuses to start when the database migration history contains IDs the build does not know
  (`Infrastructure/DatabaseStartupMigrator.cs`). **The restored database and the deployed build must match.**
- **Secrets and configuration:** the connection string, the `appsettings.Production.json` / environment
  variables in use, the TLS certificate, and the PostgreSQL superuser or database-owner credentials.
- **Backups:**
  - latest `*.dump` from `ops/backup-db.*`
  - latest `Data-<timestamp>` mirror from `ops/backup-files.*`
  - a copy of the **data-protection key folder** (`DP_KEYS_DIR`)
  - any data roots that live outside the mirrored data root

## 2. Scripts

| Script | Inputs (Linux env var / PowerShell parameter) | Behaviour |
| --- | --- | --- |
| `ops/backup-db.sh` | `PGBIN` (`/usr/bin`), `PGHOST` (`localhost`), `PGPORT` (`5432`), `PGDATABASE` (`ProjectManagement`), `PGUSER` (`pm_backup`), `PM_BACKUP_DIR` (`/srv/projectmanagement-data/backups/db`) | `pg_dump --format=custom` to `<dir>/<db>-yyyyMMdd-HHmmss.dump`. |
| `ops/backup-db.ps1` | `-PgBin` (`C:\Program Files\PostgreSQL\16\bin`), `-Host`, `-Port`, `-DbName`, `-User` (`pm_backup`), `-BackupDir` (`D:\ProjectManagementData\backups\db`) | Same as above. **Known defect:** it declares a `$Host` parameter, which collides with PowerShell's read-only automatic `$Host` variable. Expect it to fail with "Cannot overwrite variable Host"; see §6. |
| `ops/restore-db.sh` | `PM_DUMP_FILE` (required), `PGRESTORE_USER` (`postgres`), `PGBIN`, `PGHOST`, `PGPORT`, `PGDATABASE` | `dropdb --if-exists`, `createdb`, then `pg_restore --clean --if-exists`. **Destructive.** |
| `ops/restore-db.ps1` | `-DumpFile` (required), `-User` (`postgres`), `-PgBin`, `-Host`, `-Port`, `-DbName` | Same as above, with the same `$Host` defect. |
| `ops/backup-files.sh` | `PM_DATA_ROOT` (`/srv/projectmanagement-data`), `PM_FILE_BACKUP_ROOT` (`/mnt/pm-backups/files`) | `rsync -a --delete` of the data root into a new `Data-<timestamp>` folder. |
| `ops/backup-files.ps1` | `-DataRoot` (`D:\ProjectManagementData`), `-BackupRoot` (`E:\PM-Backups\files`) | `robocopy /MIR` into a new `Data-<timestamp>` folder. Log: `<BackupRoot>\backup.log`. Stops on robocopy exit code > 3. |
| `ops/restore-files.sh` | `PM_FILE_BACKUP_SOURCE` (required), `PM_DATA_ROOT` | `rsync -a --delete` from the snapshot into the data root. **Deletes files not in the snapshot.** |
| `ops/restore-files.ps1` | `-BackupSource` (required), `-DataRoot` | `robocopy /MIR` from the snapshot. **Deletes files not in the snapshot.** |

Provide the database password with `PGPASSWORD` or a `.pgpass` / `pgpass.conf` file. The scripts never
prompt for or store it. None of the scripts delete old backups, so schedule your own retention.

The Windows default `DataRoot` (`D:\ProjectManagementData`) differs from the committed
`appsettings.Production.json` (`F:/ProjectManagementData`). Always pass `-DataRoot` explicitly.

## 3. Scenarios

### 3.1 Full server loss

1. Install the OS, IIS with the .NET 8 Hosting Bundle, PostgreSQL (same major version) and `ocrmypdf`.
2. Recreate the data folders and restore the file mirror with `ops/restore-files.*`.
3. Restore the data-protection key folder to the path that `DP_KEYS_DIR` will point to.
   Without it, users must sign in again and open forms lose their antiforgery tokens.
4. Create the application database role, then restore the database (`ops/restore-db.*`).
   The dump keeps object ownership, so create the owning role first or run `pg_restore --no-owner` by hand.
5. Deploy the matching publish folder and apply the configuration (connection string, data roots, `DP_KEYS_DIR`).
6. Start the site. Startup applies any migrations the restored database is missing and validates the schema.
   If it fails, read `<Storage:DataRoot>\startup-diagnostics\startup-failure-*.log`.
7. Smoke test:
   - log in
   - open projects and download attachments
   - upload a small file and confirm it appears under the upload root
   - check **System health** (`/Admin/Diagnostics/DbHealth`)

### 3.2 Database-only rollback

1. Stop the site (app pool or service). `dropdb` fails while sessions are connected.
2. Run `ops/restore-db.*` with the chosen dump.
3. If the dump is from an older release than the deployed build, startup re-applies the newer migrations.
   If the dump is from a **newer** release, deploy that release's build instead.
4. Start the site and validate.

### 3.3 File-only rollback

The restore scripts mirror the **whole** data root. To restore a single subfolder, copy it by hand
from the `Data-<timestamp>` snapshot, for example with `robocopy <snapshot>\uploads\projects <root>\uploads\projects /E`.
Stop the site first and verify links afterwards.

## 4. Testing cadence

- Quarterly: do a full restore to an isolated server and record the recovery time.
- Monthly: restore the latest dump to a staging database (name it `prism_test_*` if you reuse it for
  the migration integration test) and run smoke tests.
- After each scheduled job: check that a new dump or snapshot exists and that the job exit code is 0.

## 5. Troubleshooting

- `pg_restore` errors about versions: the server's major version is older than the dump's.
- Startup stops with "Migration assembly and immutable manifest disagree" or reports unknown applied migrations:
  the build and the database come from different releases.
- Upload failures or missing thumbnails: the app-pool identity lacks Modify rights on the data roots.
- Keep at least two historical dumps and snapshots offline.

## 6. Known script defects

- `ops/backup-db.ps1`, `ops/restore-db.ps1`: the `$Host` parameter name collides with the automatic `$Host`
  variable. Until it is renamed (for example to `$DbHost`), run `pg_dump` / `dropdb` / `createdb` /
  `pg_restore` directly with the same arguments.
- `ops/restore-db.*`: `dropdb` is run without `--force`, so it fails while the app is connected. Stop the app first.
