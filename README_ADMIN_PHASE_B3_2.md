# PRISM Admin Phase B3.2
## Calendar recovery, Holiday administration and PDF-ingestion coordination

Apply this package after:

1. Conference Phase 3.7
2. Admin Phase A
3. Admin Phase B and its build/DI hotfixes
4. Admin Phase B3.1

Copy the package contents into the ProjectManagement project root and allow files to replace the matching paths.

## Scope delivered

### Deleted calendar events

- Dedicated `ICalendarRecoveryService` command/query boundary.
- Recovery policy enforced through `Admin.Recovery.Manage`.
- Search, category filtering and bounded paging.
- Original schedule, category, location, recurrence, deletion time and deleting user shown.
- Atomic restore predicate prevents two administrators from restoring the same event twice.
- Restore and audit write are committed in one database transaction.
- Safe error messages include a trace reference; infrastructure details remain in logs.

### Holidays

- Dedicated `IHolidayAdminService`.
- Existing Admin/HoD access retained through `Admin.Holidays.Manage`.
- Year filter, create, edit and delete workflows.
- Trimmed/collapsed names and database-backed duplicate-date protection.
- Optimistic concurrency through `Holiday.RowVersion`.
- Create/update/delete and audit records commit atomically.
- Namespaced flash messages prevent unrelated status-message leakage.

### External PDF ingestion

- Dedicated `IPdfIngestionCoordinator`.
- One in-process run at a time through a singleton run gate.
- Existing source links are skipped before files are opened.
- Separate counts for discovered, ingested/linked, already linked, missing and failed items.
- Bounded failure details with safe messages.
- Request cancellation is honoured.
- Start, completion, partial completion, cancellation and failure are audited.
- A failed start-audit prevents an unaudited ingestion run.
- Change tracking is cleared between items to control memory growth.

This phase establishes the command boundary needed for a later background-job implementation. The ingestion run remains a foreground administrative request in this package.

## Migration

The package adds:

`20261201200000_AdminPhaseB32CalendarAndIngestion`

It adds a required `RowVersion` column to `Holidays`. Existing rows are backfilled before the column is made non-nullable.

The application startup migrator should apply this migration automatically. Confirm that it is applied after deployment.

## Build and validation

Run from the project root:

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
```

If the project build invokes the Notebook asset target, run `npm ci` first when `node_modules` is absent.

## Smoke test

1. Open Admin > Deleted events; filter and restore one deleted event.
2. Confirm a second restore attempt is rejected and an audit entry exists.
3. Open Holidays as Admin and HoD; create, edit and delete a test holiday.
4. Open the same Holiday edit page in two browser tabs; save one, then verify the stale tab reports a concurrency conflict.
5. Open Admin > Ingest PDFs and start a run.
6. Verify source summaries, already-linked counts and missing/failed details.
7. Attempt a concurrent run from another browser; verify it is rejected.
8. Review Admin Logs for the corresponding audit actions.
