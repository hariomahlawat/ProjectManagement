# Production Readiness & Backup/Restore Plan

**Application:** ProjectManagement (ASP.NET Core + PostgreSQL)

---

## 1. Purpose
This document equips both engineering and operations teams with a production-ready strategy for protecting every persistent asset that powers ProjectManagement. It clarifies what must be captured in backups, how the current design influences disaster recovery, which development tasks close remaining gaps, and which day-to-day procedures operations teams can run to back up and restore the system confidently on-premises.

---

## 2. Scope
A complete backup of ProjectManagement must include all application data that is **not** rebuilt automatically from source control:

1. **PostgreSQL** – the schema, reference data, transactional records, and migrations history.
2. **Uploaded files** – every module that allows uploads (projects, visits, FFC, document repository, etc.).
3. **Configuration and deployment assets** – `appsettings.*.json`, deployment scripts, SSL certificates, service files, and backup scripts themselves.

Source code and build outputs should remain in Git/CI artifacts, yet archiving the shipped build alongside backups helps accelerate bare-metal restores.

---

## 3. Backup-sensitive architecture overview

### 3.1 Database
* Entity Framework Core migrations initialize/upgrade the PostgreSQL schema but do **not** perform backups.
* Operations must rely on PostgreSQL-native tooling (`pg_dump`, `pg_restore`, WAL archiving) for protection.

### 3.2 File storage
* Upload services (photos, videos, documents, attachments) obtain their storage root through `UploadRootProvider`, which honors the `PM_UPLOAD_ROOT` environment variable, `ProjectPhotos:StorageRoot`, or the `/var/pm/uploads` fallback.
* Subfolders (`projects`, `videos`, etc.) are created lazily under that root, but multiple modules still specify their own subpaths.
* Individual file paths are persisted in the database and must remain relative so that a restore to a different volume or server succeeds without rewriting records.

### 3.3 Observed risks
* Absolute file paths in the database would tie records to a single drive letter.
* Upload subtrees may still live under the web root on development machines; binaries should remain read-only while uploads land in a writable data directory.
* There was no shared runbook or automation for backups/restores; operators had to craft ad-hoc commands.

---

## 4. Target state design

### 4.1 Central data root
* Configure a single `Storage:DataRoot` (e.g., `D:/ProjectManagementData` on Windows, `/srv/projectmanagement-data` on Linux).
* Treat `PM_DATA_ROOT` as the authoritative environment variable for the server and backup scripts; point it at the same locatio
n as `Storage:DataRoot`.
* All module-specific uploads must live within subfolders of this root to keep file backups simple.
* Set the runtime roots so they stay under this directory:
  * `PM_UPLOAD_ROOT` → `${PM_DATA_ROOT}/uploads` (or equivalent path) so `UploadRootProvider` resolves to the same tree used by b
ackup scripts.
  * `DocRepo:RootPath` → `${PM_DATA_ROOT}/DocRepo`.
  * `ProjectDocuments:Ocr:WorkRoot` and any OCR work roots → `${PM_DATA_ROOT}/project-ocr`, `${PM_DATA_ROOT}/DocRepo/ocr-work`,
 etc.
* Include the example layout in `appsettings.Production.json` (see repo) so new environments inherit the convention automatically
.

### 4.2 Relative database paths
* Persist only relative paths per module (e.g., `projects-photos/2025/04/abc123.webp`).
* At runtime, combine the module’s root (`Path.Combine(Storage.DataRoot, RelativeModuleRoot)`) and the stored relative path.
* Provide migrations/scripts that strip hard-coded prefixes from existing data if absolute paths already exist.

### 4.3 Standard directory tree
```
DataRoot/
├─ projects-photos/
├─ document-repository/
├─ ffc-attachments/
├─ visit-photos/
└─ logs/
```
* No module should write outside this tree.
* Logs under `logs/` can be rotated independently but should follow the same backup cadence if storage allows.

### 4.4 Backup automation
* Nightly logical database dump using `pg_dump` (custom format) stored under `DataRoot/backups/db`. Configure the Linux scripts w
ith `PM_BACKUP_DIR` and Windows scripts via parameters so dumps land beside the file snapshots.
* Nightly file synchronization of `DataRoot` to a secondary volume or NAS using `robocopy` (Windows) or `rsync` (Linux). Provide
 `PM_FILE_BACKUP_ROOT` (Linux) or the PowerShell `BackupRoot` parameter, and keep it separate from the live data root.
* Weekly offline/offsite copy (external disk, secondary site) for ransomware resilience.

### 4.5 Restore discipline
* Scripts under `/ops` wrap `pg_dump`, `pg_restore`, `robocopy`, and `rsync` with consistent parameters.
* Disaster Recovery (DR) runbook describes the step-by-step rebuild path, post-restore validation, and recommended fire drills.

---

## 5. Engineering action items

1. **Introduce `StorageOptions`** with a `DataRoot` property, bind from configuration, and inject anywhere uploads occur.
2. **Refactor module options** (project photos, documents, videos, FFC, visits, etc.) so they describe *relative* subfolders under `DataRoot`.
3. **Normalize persisted paths** to relative strings. Backfill existing rows through migrations or SQL scripts.
4. **Enforce directory boundaries** (unit/integration tests ensuring upload paths resolve under `StorageOptions.DataRoot`).
5. **Publish automation** (PowerShell/Bash scripts) within `ops/` and keep them updated as connection strings or folder names evolve.
6. **Author & maintain DR documentation** (this file plus `docs/disaster-recovery.md`).
7. **Perform a full-scale restore rehearsal** at least once before production cutover and quarterly thereafter.

---

## 6. Operational procedures

### 6.1 Regular backups
* **Database:** Schedule `ops/backup-db.ps1` or `ops/backup-db.sh` nightly via Task Scheduler or cron. Store dumps on a separate volume with 30-day retention. Document the required environment variables (`PGBIN`, `PGHOST`, `PGPORT`, `PGDATABASE`, `PGUSER`, `PM_BACKUP_DIR`) alongside the job definition so rotations remain portable.
* **Files:** Mirror `Storage:DataRoot` using the provided file backup scripts. Keep at least the last 7 nightly copies plus 4 weekly copies. For Linux, set `PM_DATA_ROOT` (source) and `PM_FILE_BACKUP_ROOT` (destination); for Windows supply `DataRoot`/`BackupRoot` parameters or matching environment variables.
* **Configs & certificates:** Include `appsettings.Production.json`, SSL certs, and service definitions in the same backup job or a dedicated secure repository.

### 6.2 Restore runbook (abridged)
1. Provision OS, .NET Runtime, and PostgreSQL (matching major versions).
2. Create/restore `Storage:DataRoot` from the latest file backup. On Linux this means exporting `PM_FILE_BACKUP_SOURCE` to the desired timestamped mirror before running `ops/restore-files.sh`; on Windows pass the equivalent `BackupSource` parameter to the PowerShell script.
3. Deploy the published application build.
4. Recreate the PostgreSQL database and run `ops/restore-db.*` with the chosen dump.
5. Drop the restored `appsettings.Production.json` (or environment variables) on the server, pointing to the restored database and data root.
6. Start the application service (IIS site, systemd service, Windows Service, etc.).
7. Perform smoke tests: login, open projects, download attachments, verify uploads.

### 6.3 Disaster recovery drills
* Twice per year (minimum), execute the full restore to a staging server. Capture gaps, update scripts/docs, and verify time-to-recover against SLA.

---

## 7. Security and compliance
* Use dedicated PostgreSQL roles for backup/restore scripts; never embed credentials in scripts—read from environment variables or secure vaults.
* Encrypt backups at rest (disk encryption, encrypted shares) and in transit (SFTP, SMB over VPN).
* Limit permissions on `Storage:DataRoot` to the application identity and backup account.
* Validate upload content (MIME filtering, antivirus) and honor ASP.NET Core’s request size limits in production.

---

## 8. Future enhancements (not in scope yet)
* Automate point-in-time recovery with WAL archiving or streaming replication.
* Integrate monitoring for backup job success/failure (Prometheus exporters, Windows Event Log alerts).
* Provide an administrative UI for viewing backup status once infrastructure-level automation proves stable.

---

## 9. References
* `docs/disaster-recovery.md` – hands-on restore instructions.
* `ops/backup-*.ps1/.sh` – automation entry points for scheduling.
* `Services/Storage/UploadRootProvider.cs` – runtime logic for resolving the upload root.
* `docs/configuration-reference.md` – configuration keys, including `ProjectPhotos:StorageRoot`.

