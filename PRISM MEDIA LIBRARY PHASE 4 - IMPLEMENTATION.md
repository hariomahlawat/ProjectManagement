# PRISM Media Library Phase 4

## Scope

This phase operationalises the optional media catalogue and removes the deployment dead-end shown by the “catalogue unavailable” banner.

## Delivered

- Controlled media-catalogue schema readiness service.
- PostgreSQL advisory lock around catalogue migrations.
- Optional startup migration (`MediaLibrary:AutoMigrate`).
- Development defaults to automatic migration.
- Production remains explicit and controlled.
- Protected Admin/HoD “Initialize catalogue” action.
- Exact pending-migration and connection diagnostics.
- Core PRISM Photos remains available when the optional catalogue is absent or migration fails.
- Phase 3 unified PostgreSQL query and indexes are included.

## Production deployment

Preferred automated deployment command:

```powershell
dotnet ef database update --context MediaLibraryDbContext
```

Alternatively, an Admin or HoD may open `/Admin/MediaSources` and select **Initialize catalogue**. This action is protected by the existing role policy and antiforgery validation.

`AutoMigrate` remains `false` in production. Set it to `true` only where automatic application-start migration is an approved deployment policy.

## External folders

External folders remain disabled by default. Catalogue initialization does not connect, scan, copy, modify or delete any external file.
