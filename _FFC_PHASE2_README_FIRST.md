# FFC Phase 2 — Portfolio Overview

This package is designed to be copied over the **ProjectManagement project root** after applying the FFC Phase 1 foundation package.

## Scope

- Replaces the oversized FFC country/year card grid with compact expandable portfolio rows.
- Adds global filtered metrics and a segmented installed/delivered/planned unit summary.
- Replaces the filter modal with a persistent search/filter bar, advanced filters and individually removable filter chips.
- Loads the Overview through bounded DTO-based queries and batch-resolves canonical project progress through `IFfcProgressService`.
- Increases Overview pagination to 25 records per page.
- Adds scoped Overview CSS and a small progressive-enhancement script.
- Adds service, PageModel and Razor contract tests.

## Deliberately unchanged

The following Detailed Table assets are not included and were not modified:

- `Areas/ProjectOfficeReports/Pages/FFC/MapTableDetailed.cshtml`
- `Areas/ProjectOfficeReports/Pages/FFC/_DetailedTablePartial.cshtml`
- `wwwroot/js/pages/project-office-reports/ffc/ffc-map-table-detailed-inline-edit.js`

The Detailed Table columns, grouping, inline editing and Excel export therefore remain unchanged.

## Database

No migration is required for this phase.

## Apply

1. Stop the application or IIS application pool.
2. Back up the solution and database.
3. Copy the contents of this folder into the project root, preserving the folder structure and replacing matching files.
4. Build and test:

```powershell
dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release
```

5. Start the application and hard-refresh the browser so the new versioned CSS and JavaScript are loaded.

## Acceptance checks

- `/ProjectOfficeReports/FFC` displays compact country-year rows rather than the old tall cards.
- Search covers country name, ISO code, FFC project name and linked PRISM project name.
- Summary figures remain global when the result set spans more than one page.
- Delivery and installation filters support Pending, Partial and Completed.
- Expanding a row shows the full project roll-up without leaving the page.
- Linked project progress matches the Detailed Table because both use the same canonical external remark.
- Detailed Table appearance and behaviour remain unchanged.

## Verification note

JavaScript syntax, XML validity, source delimiter balance, changed-file paths and Detailed Table checksums were verified in the preparation environment. The environment does not contain the .NET SDK, so `dotnet build` and `dotnet test` must be run on the development machine before deployment.
