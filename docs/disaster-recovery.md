# Disaster Recovery Runbook

This runbook provides the operational checklist for restoring ProjectManagement after hardware loss, OS corruption, or accidental data deletion. It assumes backups were created using the scripts in the `ops/` folder and stored on secondary media.

---

## 1. Pre-requisites

### 1.1 Software versions
* OS: Windows Server 2022 or Ubuntu 22.04 LTS (match production choice).
* .NET Runtime: version defined in `global.json`/`Directory.Build.props` for ProjectManagement.
* PostgreSQL: **same major version** as production (currently 16.x).

### 1.2 Accounts & secrets
* PostgreSQL superuser (or owner) credentials.
* Backup role password (for `pg_dump`).
* SSL certificates (if HTTPS termination happens on the app host).
* Application secrets for `appsettings.Production.json` (connection string, SSO, SMTP, etc.).

### 1.3 Backup assets
* Latest database dump from `ops/backup-db.*`.
* Latest file archive/mirror from `ops/backup-files.*`.
* Last known-good `appsettings.Production.json` and deployment scripts.

---

## 2. Restore scenarios

### 2.1 Full server loss (bare-metal)
1. **Rebuild infrastructure**
   * Install OS updates.
   * Install .NET Hosting Bundle.
   * Install PostgreSQL (matching major version), create admin user.
2. **Restore `Storage:DataRoot`**
   * Create the designated root (e.g., `/srv/projectmanagement-data`).
   * Copy the latest file backup into place (mirror or extract archive).
3. **Deploy application**
   * Pull release artifact from CI or build server.
   * Publish to IIS/site folder or `/var/www/projectmanagement` as applicable.
4. **Restore PostgreSQL**
   * Run `ops/restore-db.ps1` or `.sh` pointing at the `.dump` file. Provide admin credentials when prompted or via environment variables.
5. **Apply configuration**
   * Place `appsettings.Production.json` (or environment-specific config) with accurate connection string and `Storage:DataRoot` path.
6. **Start services**
   * Start PostgreSQL (if not already) and the ProjectManagement app service.
7. **Smoke tests**
   * Login, open dashboards, browse multiple modules, download attachments.
   * Attempt a small upload and confirm the file appears under the restored `DataRoot`.

### 2.2 Database-only rollback
1. Stop the application service to avoid writes.
2. Run the DB restore script against the target dump.
3. Start the application and validate data.

### 2.3 File-only rollback
1. Stop the application service.
2. Restore only the affected subfolders from the file backup (e.g., `projects-photos`).
3. Start the service and verify links/files.

---

## 3. Scripts reference

| Script | Purpose | Key parameters |
| --- | --- | --- |
| `ops/backup-db.ps1` / `.sh` | Nightly PostgreSQL logical backup using `pg_dump` (custom format). | Host, port, DB name, backup directory, DB role. |
| `ops/restore-db.ps1` / `.sh` | Restores database dump using `pg_restore`. | Host, port, DB name, dump file path, admin role. |
| `ops/backup-files.ps1` / `.sh` | Mirrors `Storage:DataRoot` to backup location using `robocopy` or `rsync`. | Source data root, destination root, retention policy. |
| `ops/restore-files.ps1` / `.sh` | Copies backed-up files back into `Storage:DataRoot`. | Backup location, target data root. |

Always run scripts from an elevated prompt or with permissions sufficient to read/write the relevant folders.

---

## 4. Testing cadence
* **Quarterly DR drill:** Execute full restore to an isolated server and measure recovery time objective (RTO).
* **Monthly validation:** Restore the latest backup to a staging database and run automated smoke tests.
* **Continuous monitoring:** After each scheduled backup, verify the presence of a new dump/rsync folder and check script exit codes/logs.

---

## 5. Troubleshooting tips
* If `pg_restore` reports schema mismatches, confirm the PostgreSQL major version matches the one used for the dump.
* When file paths fail after restore, inspect the database for absolute paths and normalize them with SQL scripts (strip prior prefixes).
* Ensure the application identity has read/write access to `Storage:DataRoot`; permission issues manifest as upload failures or missing thumbnails.
* Keep at least two historical dumps and file copies offline to mitigate silent corruption or ransomware.

---

## 6. Change management
* Update this runbook whenever configuration keys, deployment topology, or backup destinations change.
* Version control the `ops/` scripts alongside the application so changes are reviewed and tested.

