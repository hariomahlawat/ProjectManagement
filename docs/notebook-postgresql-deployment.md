# Notebook PostgreSQL deployment policy

## SECTION: UUID generation extension policy

Notebook version repair migrations use PostgreSQL `gen_random_uuid()` and therefore execute `CREATE EXTENSION IF NOT EXISTS pgcrypto;` before repairing missing `NotebookItems.Version` values.

The deployment policy is **Policy B — migration user authorised**:

- The database role that runs Entity Framework migrations must be allowed to create the `pgcrypto` extension, or the extension must already be installed by a DBA before migrations run.
- Runtime application roles do not require extension-creation privileges.
- Offline deployments must include this privilege check in the database preparation checklist before applying Notebook migrations.

If a target environment cannot grant extension creation to the migration role, a DBA must provision `pgcrypto` manually before migration execution while retaining the migration command as an idempotent safeguard.
