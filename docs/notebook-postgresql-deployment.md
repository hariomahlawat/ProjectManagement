# Notebook PostgreSQL deployment policy

## UUID generation extension (`pgcrypto`)

Migrations `20261125231000_AddNotebookItemVersion` and `20261125232000_RepairMissingNotebookItemVersion`
run `CREATE EXTENSION IF NOT EXISTS pgcrypto;` and use `gen_random_uuid()` to back-fill
`NotebookItems.Version`.

Policy: **the migration role is authorised to create extensions**.

- Migrations are applied automatically and mandatorily at application startup, so the database
  role used by the application connection must be allowed to create `pgcrypto`, **or** a DBA must
  install `pgcrypto` in the target database before the first start of this version.
- Once the extension exists, `CREATE EXTENSION IF NOT EXISTS` is a no-op and no extension privilege
  is needed.
- Offline deployments must include this check in the database preparation checklist.

(PostgreSQL 13+ also provides `gen_random_uuid()` in core; the extension statement is still
executed, so the privilege or pre-installation is still required.)

## Startup schema check

`Program.EnsureNotebookVersionSchemaAsync` stops startup if `AddNotebookModule` and
`AddNotebookItemVersion` are missing or out of order, or if `NotebookItems.Version` is not a
non-null `uuid` column.

See `docs/notebook.md` for the feature architecture.
