# Apply instructions

This package is a ready-to-paste delta for the current PRISM Photos codebase after **Organisation-wide Albums v2 / CS0173 hotfix**.

Copy the package contents over the project root, for example:

```text
E:\Dot Net Web Development\ProjectManagement\
```

Overwrite matching files. New files will be created in their preserved project paths.

## No database migration

This phase requires no EF migration and no appsettings change.

## Recommended verification

```powershell
dotnet clean

Remove-Item .\bin, .\obj `
    -Recurse -Force `
    -ErrorAction SilentlyContinue

dotnet build .\ProjectManagement.csproj

dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\photos-library.js
node --check .\wwwroot\js\pages\photos-curation.js
node --test .\wwwroot\js\pages\photos-curation-contract.test.js
```

## Functional smoke test

1. Open a manageable empty album: **Add media** is shown; **Organise** is not.
2. Press **Add media**: Photos opens directly in selection mode with the album named in the context strip.
3. Select media and press **Add selected**: the user returns to that album and the new media is present.
4. Re-enter Add media: existing album items are visibly marked **In album** and cannot be selected again.
5. Add a second media item: **Organise** becomes available.
6. Verify search/filter/sort and **Clear filters** retain target-album mode until Cancel or successful add.
7. Verify albums belonging to another ordinary user remain viewable but not manageable; Admin/HoD/Comdt can manage them.
