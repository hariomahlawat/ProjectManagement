# FFC Projects Update — Ready-to-paste implementation

This bundle contains complete replacement/new files for the uploaded PRISM ERP source tree.

## Installation

1. Take a backup of the current project.
2. Open this bundle and copy its folders/files into the **project root** (the folder containing `ProjectManagement.csproj`).
3. Allow Windows to merge folders and replace existing files.
4. No database migration is required.
5. Build and test:

```powershell
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

## Implemented scope

- Country–year headings link to the corresponding FFC record and preserve a local `returnUrl` back to the detailed table.
- Compact Export menu with Word and Excel options.
- Native editable Open XML Word export titled **FFC Projects Update**.
- A4 landscape Word layout, portfolio summary, real seven-column table, repeating header row, country–year separator rows, full narratives, page fields, PRISM footer and optional authorised handling/classification marking.
- Professional Excel register with full narratives, numeric cells, auto-filter, frozen panes and landscape print setup.
- Sticky desktop table header without an inner vertical scroll region.
- Refined headings: **Current progress** and **Overall status**.
- Right-aligned cost/quantity with tabular numerals.
- Full status retained in the page with overflow-aware **Show full... / Show less** controls.
- Dedicated A4 landscape print stylesheet.
- Existing inline editing, current route scope and permissions retained.
- No permanent filter toolbar and no database migration.

## Verification performed in the delivery workspace

- JavaScript syntax validation passed for both detailed-table scripts.
- Project XML and CSS parsing passed.
- Static implementation-contract and source-balance checks passed.
- The delivery workspace did not contain the .NET SDK, so `dotnet build` and `dotnet test` could not be executed there. Run the two commands above after replacement.

`CHANGED-FILES.txt` lists every project file in this bundle. `SHA256SUMS.txt` provides file hashes for integrity checking.
