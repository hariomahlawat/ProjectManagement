# Validation record

## Completed in the review workspace

- Compared the implementation tree against a fresh extraction of the supplied source archive.
- Confirmed the overlay contains only the intentional Compendium changes listed in
  `FILE-MANIFEST.md`.
- `node --check wwwroot/js/pages/projects-compendium-cover-editor.js` — passed.
- `node --check wwwroot/js/projects/compendium-cover-editor-state.js` — passed.
- `node --test --test-reporter=dot wwwroot/js/projects/*compendium*.test.js` — **260/260 passed**.
- Performed delimiter/lexical checks over every changed C# source file — passed.
- Verified the original six DM Sans files are present in
  `wwwroot/fonts/publications/dm-sans`.
- Verified the ready-to-paste overlay is byte-identical to the reviewed implementation sources.
- Dry-ran and applied `CHANGESET.patch` to a fresh extraction of the supplied archive; the patched
  tree compared byte-for-byte with the reviewed implementation tree.

## Required on a Windows/.NET build machine

The review container did not provide the .NET SDK or PowerShell, so it could not honestly execute a
C# compile, xUnit, Windows self-contained publish, native SkiaSharp load, or the generated EXE. Run
these release gates before deployment:

```powershell
dotnet restore .\ProjectManagement.sln
dotnet build .\ProjectManagement.sln -c Release --no-restore
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --no-build
.\ops\publish\create-publish-folder.ps1
.\ops\publish\test-compendium-offline-payload.ps1 -PublishRoot .\artifacts\publish\ProjectManagement
```

The last two commands exercise the actual win-x64 payload without starting IIS, accessing the
database or using the Internet.
