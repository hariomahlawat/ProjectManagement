# PRISM Media Availability Finalisation

Copy all folders in this package into the ProjectManagement root and replace existing files.

## Implemented

- Historical dead-letter records are reconciled against their actual source files.
- Missing, access-denied, temporary, unsupported and corrupt sources receive distinct availability states.
- Unavailable media is excluded from Photos through the existing catalogue availability filter.
- Missing media is removed from active processing diagnostics while job history is retained for audit.
- Reconciliation runs independently of the derivative-processing worker and continues periodically.
- Manual reconciliation shows the remaining historical candidate count and processes a bounded batch.
- Restored files are requeued idempotently without creating duplicate processing jobs.
- Collection covers are repaired when the former cover becomes unavailable.
- Broken image responses render a controlled “Media unavailable” placeholder instead of a browser broken-image icon.
- Global force-retry of permanent failures has been removed from the primary administration actions.

## Deployment

No database migration is required for this package.

1. Stop PRISM/IIS application pool.
2. Copy the package contents into the ProjectManagement root.
3. Run `dotnet clean`, `dotnet restore`, `dotnet build`, and `dotnet test`.
4. Restart PRISM.
5. Open `/Admin/MediaSources`.
6. Use **Reconcile availability** once for immediate processing; the background worker also continues automatically.

Expected after reconciliation: historical source-missing assets move from Available catalogue to Unavailable media and disappear from the normal Photos timeline.
