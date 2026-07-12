# PRISM Admin Phase B3.1 — Master-Data Hardening

## Application order

Apply this package after:

1. Conference Phase 3.7
2. Admin Phase A
3. Admin Phase B
4. Admin Phase B build hotfix
5. Admin Phase B Workspace DI hotfix

The archive preserves project-relative paths. Copy its contents over the project root and allow replacement of existing files.

## Scope

This phase hardens the master-data administration used by Projects and Activities:

- Project Categories
- Technical Categories
- Project Types
- Sponsoring Units
- Line Directorates
- Activity Types

It also replaces generic TempData keys in Admin Users and Celebrations with namespaced keys so messages cannot leak to unrelated pages.

## Main changes

- Shared master-data name normalisation.
- Case-insensitive, trimmed database uniqueness.
- Shared hierarchy validation for Project and Technical Categories.
- Self-parent, descendant-parent and hierarchy-cycle protection.
- Optimistic concurrency tokens for categories and flat project lookups.
- Central Admin audit events for create, update, status, delete and reorder operations.
- Controlled duplicate, in-use, not-found and concurrency responses.
- Consistent Admin master-data policy enforcement.
- Activity Type duplicate-race and concurrency handling.
- Namespaced flash-message keys for Admin Users, Admin Master Data and Celebrations.
- The Celebrations page now consumes its own success/error messages, resolving the observed `Birthday added.` message on the Users page.

## Database migration

The package adds:

`20261201190000_AdminPhaseB3MasterDataHardening`

The migration adds `RowVersion` columns to:

- `ProjectCategories`
- `TechnicalCategories`
- `ProjectTypes`
- `SponsoringUnits`
- `LineDirectorates`

It also creates case-insensitive functional unique indexes for the above name fields and `ActivityTypes.Name`.

Before creating the indexes, the migration checks for existing case-insensitive duplicates. If duplicates exist, migration intentionally stops with a precise diagnostic rather than choosing or deleting data automatically.

The normal PRISM startup migrator should apply the migration during deployment.

## Validation commands

Run from the solution directory:

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
```

Then verify:

1. Create/edit Project and Technical Categories.
2. Attempt to move a category under its own descendant; the operation must be rejected.
3. Open the same lookup in two browser sessions, update in one, and submit the stale form in the other; a concurrency message must be shown.
4. Attempt a case-only duplicate name, including leading/trailing whitespace; it must be rejected.
5. Create, update and reorder Project Types, Sponsoring Units and Line Directorates.
6. Create/update Activity Types and verify duplicate names are rejected.
7. Add a Birthday or Anniversary and verify its confirmation appears only on Celebrations, never on Admin Users.
8. Review Admin Logs for the new `MasterData.*` audit actions.

## Deployment note

Take a database backup before deployment. If the migration reports duplicate master-data names, resolve those records deliberately and restart the application. Do not modify the immutable migration ID or edit an already-applied migration.
