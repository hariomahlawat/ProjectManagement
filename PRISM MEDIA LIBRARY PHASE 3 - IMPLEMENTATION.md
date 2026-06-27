# PRISM Media Library Phase 3

## Scope

This release moves the Photos page to a single PostgreSQL-backed media catalogue query.
Project photos, project videos, visit photos, social-media event photos and optional
external-folder media are now filtered, sorted, counted and paged through one read model.

## Key behaviours

- Cross-source chronological ordering is stable.
- Search, source, project, year, kind and classification filters execute in PostgreSQL.
- Page counts, photo/video totals and collection counts come from the same filtered query.
- External folders remain optional and disabled by default.
- If the media catalogue is unavailable or has not completed its first PRISM sync, the
  Photos page falls back to direct PRISM-owned media queries.
- No external-folder failure can prevent normal PRISM photos from being browsed.
- Timeline query indexes are added through a dedicated MediaLibraryDbContext migration.

## Safe configuration

The checked-in settings use the nested Phase 2/3 configuration model. External folders
are disabled and the source list is empty. Configure folders through `/Admin/MediaSources`
after enabling `MediaLibrary:ExternalSources:Enabled` and
`MediaLibrary:ExternalSources:ScannerWorkerEnabled`.

## Deployment

```powershell
dotnet restore
dotnet ef database update --context MediaLibraryDbContext
dotnet build
dotnet test
```

Restart PRISM after applying the migration. The periodic PRISM catalogue reconciliation
runs every five minutes and remains the recovery mechanism for missed source updates.
