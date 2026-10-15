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
   * Stop the application and close database sessions. Run `ops/restore-db.ps1` or `.sh` pointing at the trusted `.dump` file; follow the staged restore procedure below. Supply credentials through a local password file or environment, not a command line. The scripts use `--no-password` and fail rather than prompt.
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
3. Check database-level settings and application-role access, then start the application and validate data. Keep the retained previous database until those checks pass.

### 2.3 File-only rollback
1. Stop the application service.
2. Restore only the affected subfolders from the file backup (e.g., `projects-photos`).
3. Start the service and verify links/files.

### 2.4 Staged database restore and retained rollback

Both restore scripts verify `dropdb`, `createdb`, `pg_restore`, `psql` and the dump before database changes. They inspect the archive with `pg_restore --list`; listing alone does **not** prove the archive can restore. They then create a unique database from `template0`, restore into it with `--single-transaction --exit-on-error`, connect to it, and require `Projects`, `AspNetUsers` and `__EFMigrationsHistory` tables together in a user schema. This is a small structural check, not application validation or recovery certification.

Only after those checks do the scripts rename the original target to a unique `<target-stem>_previous_<UTC timestamp>_<random>` name and staging to the target. Both renames run in one transaction with identity checks before and after, so a failed second rename rolls back the first. An absent target is supported. Generated names are bounded ASCII identifiers (at most 57 bytes); leftovers from other attempts are never reused. SQL identifiers are quoted through `psql` variables. Administration connects to `postgres`, or `template1` when restoring `postgres`; template database targets are refused. The role needs create/restore/rename permissions and access to the maintenance database. PostgreSQL client tools must support `psql`'s `\getenv` (14+, including the supported PostgreSQL 16 baseline).

Stop the app, background jobs and connection pools before running restore. Active sessions or prepared transactions prevent cutover; scripts never terminate sessions or force-drop databases. Resolve those sessions deliberately, inspect the reported names, and retry. Administrative lock waits are bounded to 5 seconds and statements to 15 seconds. The restore itself may take longer. Allow storage for the original, replacement and dump simultaneously.

The old database is **retained after success**, and its exact name is printed. Failed staging databases are also retained, with their name and known OID reported. Automatic cleanup is deliberately deferred: PostgreSQL cannot execute `DROP DATABASE` within the identity-check transaction, and a separate name-based drop could race with an administrative rename and delete a different database. Inspect the failed staging identity and remove it deliberately once no concurrent administrative work is possible; never delete the target or previous copy as failed-stage cleanup. Databases with uncertain identity are retained. A cutover error, including a lost commit response, retains all copies for inspection rather than guessing which database to delete. If cutover commits but the canonical connection/schema check fails, a conditional transaction attempts to restore the original name and retains the staged copy; if identity/session checks block rollback, both copies remain and the script reports failure. Inspect names and OIDs before any manual intervention.

Before restarting PRISM, verify the application login, database owner, `CONNECT` permissions, database ACLs, locale/encoding and database/role-in-database settings (including `search_path`) against the deployment configuration and retained original. New staging uses `template0` and the restoring role as database owner. Object grants/ownership are restored by `pg_restore`, but database-level grants, comments and settings are **not** copied by this flow, which deliberately omits `--create` to keep the archive from choosing a database name. Reapply required database-level configuration locally before app startup. Use only trusted archives: restoring SQL with privileged credentials is not a sandbox.

After application smoke tests, data checks and an agreed retention period, an operator may deliberately remove the printed previous database. Confirm its exact name is the retained previous copy, never the active target or an unrelated leftover. For example, from a maintenance connection, use `dropdb --host=<LAN-host> --port=<port> --username=<admin> --maintenance-db=postgres -- <exact-printed-previous-name>` (use `template1` if the active target is `postgres`). In PowerShell invoke the executable with `&` and quote each argument; in Bash quote paths and values. Do not add `--force`. Deleting the retained copy is a separate, irreversible operator decision. To roll back after a successful restore, stop the app and all affected connections, inspect database identities, and deliberately rename the restored target aside and the verified retained copy back, using the maintenance database and one transaction.

PowerShell keeps `-PgHost` and the legacy `-Host` alias. Bash keeps `PGBIN`, `PGHOST`, `PGPORT`, `PGDATABASE`, `PGRESTORE_USER` and `PM_DUMP_FILE`. Database names use UTF-8 hex for SQL variables and percent-encoded connection URI paths, avoiding Windows native environment encoding problems and connection-string interpretation of names containing `=`. SQL sessions use UTF-8 client encoding. Restore ignores `PGSERVICE`/`PGSERVICEFILE` and `PGHOSTADDR` so inherited defaults cannot redirect the selected database/host. It uses a 10-second connection timeout; all operations stay on the configured PostgreSQL host and require no Internet access. `dropdb` is checked during preflight for the later manual cleanup procedure; restore itself never invokes it.

PostgreSQL references: [database rename](https://www.postgresql.org/docs/16/sql-alterdatabase.html), [restore options](https://www.postgresql.org/docs/16/app-pgrestore.html), [psql variable quoting](https://www.postgresql.org/docs/16/app-psql.html), and [connection URI encoding](https://www.postgresql.org/docs/16/libpq-connect.html#LIBPQ-CONNSTRING-URIS).

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

