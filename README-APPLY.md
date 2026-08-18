# PRISM Photos — Organisation-wide Albums & Curation

## Apply

This package is a **delta over the current Photos implementation after the People Review Workflow Integrity phase**.

1. Stop the application / IIS app pool if this is the production machine.
2. Take a PostgreSQL backup before first deployment of this phase.
3. Copy the contents of this package over the PRISM project root and overwrite matching files.
4. Build and test the solution.
5. Start the application. The existing startup migration mechanism will apply `20260818170000_AddOrganisationalMediaAlbums`.
6. Verify Photos → Collections → Albums, then create a small test album and exercise Add to album / reorder / cover / archive.

No manual SQL is required when the normal PRISM startup migrator is enabled.

## Recommended verification

```powershell
dotnet clean
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
node --check .\wwwroot\js\pages\photos-library.js
```

## Permission model

- Every authenticated Photos user can view active organisation-wide albums.
- Every authenticated Photos user can create albums.
- The creator can manage their own albums.
- `Admin`, `HoD`, and `Comdt` can manage any album.
- `Admin`, `HoD`, and `Comdt` can edit the organisation-wide editorial caption on media.
- There are no personal/private/shared album states.
- Archiving an album never deletes its source media.
