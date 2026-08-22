# Validation record

## Completed in the review workspace

- JavaScript syntax validation passed for the cover state and page modules.
- Complete Compendium browser/contract suite passed: **267 tests, 267 passed, 0 failed**.
- Phase 42 focused contract suite passed: **7 tests, 7 passed, 0 failed**.
- Delimiter/static structure checks passed for every changed C# source file.
- The cumulative file set was compared against the supplied source baseline; no file deletion or database migration is introduced.

## Required on the Windows build workstation

The review workspace does not contain the .NET 8 SDK, so it could not execute Roslyn compilation or xUnit. Before publishing, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Test-PrismPublicationsPhase42.ps1
```

That script performs the complete JavaScript suite, `dotnet build --no-restore`, and the focused Phase 42 xUnit suite. `--no-restore` is intentional for an offline/controlled build; ensure the approved NuGet cache has already been restored.

Then run:

```powershell
.\ops\publish\create-publish-folder.ps1
```

The publish script creates a self-contained `win-x64` payload and executes the Compendium offline dependency/PDF self-test before the payload can be accepted.
