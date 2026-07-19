# FFC Phase 1 — Foundation implementation

This package is ready to paste over the root of the existing `ProjectManagement` repository while preserving the folder structure.

## Scope implemented

- Shared `IFfcPortfolioService` for global filtered totals and common status semantics.
- Shared `IFfcProgressService` for the intentional canonical relationship between FFC progress and the linked Project external remark.
- Batched external-remark retrieval, removing the previous per-project remark queries.
- Correct overview totals across the full filtered result set, not merely the current page.
- `Pending / Partial / Completed` delivery and installation filters.
- Search across country, ISO code, year, FFC project name, and linked PRISM project name.
- Database constraints for one active country/year record, one linked PRISM project per FFC record, installed-implies-delivered, and date ordering.
- Friendly duplicate validation in record and project management pages.
- Regression tests protecting the Detailed Table contract.

## Detailed Table protection

The following visible assets are intentionally unchanged:

- `Areas/ProjectOfficeReports/Pages/FFC/MapTableDetailed.cshtml`
- `Areas/ProjectOfficeReports/Pages/FFC/_DetailedTablePartial.cshtml`
- `wwwroot/js/pages/project-office-reports/ffc/ffc-map-table-detailed-inline-edit.js`

The table columns, grouping, row layout, inline editing interaction, handler names, and Excel export format remain unchanged. Its backend now uses the shared progress service.

## Installation

1. Back up the repository and PostgreSQL database.
2. Run `_FFC_PHASE1_PREFLIGHT.sql`. The first three result sets must be empty. Resolve any rows before deployment.
3. Copy all package contents into the project root and replace matching files.
4. From the project root, run `npm ci` if `node_modules/esbuild` is not already present.
5. Run:

```powershell
 dotnet restore
 dotnet build ProjectManagement.csproj -c Release
 dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release
```

6. Apply migration `20261201260000_HardenFfcFoundation` through the existing automatic-startup migration process, or explicitly run:

```powershell
 dotnet ef database update
```

## Migration behaviour

The migration automatically repairs the unambiguous legacy state where an item is installed but not marked delivered. It deliberately stops with a precise error rather than silently merging or deleting data when duplicate country/year records, duplicate project links, or invalid delivery/installation dates exist.

## Validation note

The package was checked for source-tree consistency, balanced C# delimiters, XML validity, migration-manifest ordering, protected Detailed Table file identity, and expected replacement paths. A .NET SDK was not available in the preparation environment, so the build and test commands above must be run on the development machine before deployment.
