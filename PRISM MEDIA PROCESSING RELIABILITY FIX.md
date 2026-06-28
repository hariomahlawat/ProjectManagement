# PRISM Media Processing Reliability Fix

## Purpose

This replacement corrects the Phase 6 background-processing blind spot where the administration page could remain at `172 queued / processing` while jobs were repeatedly failing and returning to Pending.

## Main corrections

- Separates Pending, Running, Retrying, Completed, Failed and Dead-letter counts without requiring a database migration.
- Adds an in-process worker heartbeat, current job/asset, last completion and last failure.
- Recovers expired worker locks safely.
- Handles null/legacy `AvailableAfterUtc` values during claiming.
- Preserves retry failure information instead of making retrying jobs look new.
- Sends permanently unavailable or unsupported content directly to Dead-letter rather than retrying five times.
- Returns unavailable/deleted catalogue assets as harmless no-op completions.
- Adds robust source-provider fallback chains for historical Project, Visit and Social Media photos.
- Adds a cache write/read health probe and reports the configured cache root.
- Removes eager cache-directory creation from application startup so permission errors can be diagnosed in the UI.
- Adds per-job retry from the administration page.
- Keeps all PRISM-owned media and Photos browsing independent of processing failures.

## Deployment

Copy all folders into the ProjectManagement root and replace files.

No database migration is required.

Run:

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet test
```

Restart the application. Open `/Admin/MediaSources` and review the **Media processing** section.

## Expected behaviour

- The worker status should show `Running`, `Polling`, `Processing`, or `Idle`.
- The Pending count should decrease as jobs complete.
- A failed attempt appears under Retrying with the actual error and next retry time.
- Missing historical source content moves to Dead-letter immediately and no longer blocks the rest of the queue.
- Cache permission problems are shown explicitly.

## Production cache permission

The application-pool identity must have Read/Write/Create/Delete permission on the configured `MediaLibrary:CacheRoot`.
