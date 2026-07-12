# PRISM Admin Phase B4.1 — Media Operations Decomposition

## Application order

Apply this package after:

1. Conference Phase 3.7
2. Admin Phase A
3. Admin Phase B
4. Admin Phase B build hotfix
5. Admin Phase B Workspace DI hotfix
6. Admin Phase B3.1
7. Admin Phase B3.2

Replace or add the files using the project-relative paths contained in this archive.

## Scope

This phase preserves the existing `/Admin/MediaSources` route and broadly preserves its UI while moving operational logic out of the Razor PageModel.

### New application services

- `IMediaSourcesAdminQueryService` — bounded, read-only page projection
- `IMediaSourceAdminService` — create/edit/test/scan/state/disconnect operations
- `IMediaQueueAdminService` — bounded retry operations for processing and ingestion queues
- `IMediaRecoveryAdminService` — catalogue health, synchronization, consistency and availability recovery
- `IMediaAdminAccessService` — service-level policy enforcement

### Hardening included

- Explicit media administration policies
- Service-level authorization checks
- Central Admin audit adoption for media mutations
- Shared Admin IST time handling; no server-local `.ToLocalTime()` calls
- Safe user-facing failures with trace references
- Bounded queue and recovery operations
- Queue-state validation before retry
- Server-side pagination for unavailable media
- Optimistic source-configuration concurrency tokens
- Runtime health/scan updates do not create false edit conflicts
- Configuration-managed sources remain testable and scannable, but not editable or disconnectable
- Source paths are fingerprinted rather than written to Admin audit payloads
- Namespaced TempData keys for Media Sources
- New regression tests for source concurrency and queue retry rules

## Database migration

No database migration is required.

## Local validation

Run from the solution directory:

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
```

Recommended smoke tests:

1. Open `/Admin/MediaSources` as Admin and HoD.
2. Test a configuration-managed external source.
3. Request a scan for a configuration-managed source.
4. Create and edit a database-managed external source.
5. Confirm a stale edit produces a concurrency message rather than overwriting.
6. Pause/resume and hide/show a database-managed source.
7. Retry one recoverable failed processing job.
8. Confirm a Running or Completed job cannot be retried.
9. Run catalogue test and consistency check.
10. Recheck one unavailable item and a bounded unavailable batch.
11. Confirm timestamps display in IST.
12. Confirm the Admin audit log contains media administration events without raw source paths.

## Important deployment note

The package was statically validated in the generation environment, including C# syntax parsing, Razor directive checks, DI/policy registration checks, patch application and archive checksums. The .NET SDK was not available in that environment, so the Release build and xUnit suite must be run locally before deployment.
