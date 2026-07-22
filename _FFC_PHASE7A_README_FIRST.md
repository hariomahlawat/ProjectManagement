# FFC Phase 7A — Terminology Standardisation

## Apply this package

This focused package is designed to be applied **after FFC Phase 7 — PowerPoint Briefing Export**, including the Phase 7 build correction.

Extract the ZIP into the `ProjectManagement` project root, preserve the directory structure, and replace matching files.

**No database migration is required.**

## Standard terminology

This phase standardises user-facing FFC terminology across the web module and generated PowerPoint briefings:

| Previous wording | Standard wording |
|---|---|
| Unit / Units | Quantity / Qty |
| Total units | Total quantity |
| Unit position | Quantity status |
| Position | Status |
| Current position | Current status |
| Overall position | Overall status |
| Project position | Project status |
| Country-wise unit position | Country-wise quantity status |
| IPA and GSL position | IPA and GSL status |

`Quantity status` is used for the aggregate Installed / Delivered / Planned distribution. `Status` is used for an individual project or record state.

## Areas updated

### Portfolio Overview and Footprint

- Portfolio summary now shows **Total quantity** and **Quantity status**.
- Country-year rows use **Qty**, **Current status**, and project **Status**.
- Footprint filters, map legend, country cards, and country-detail panel use quantity terminology consistently.
- Search and explanatory text refer to project quantities and status.

### Country-Year Workspace and editors

- Workspace summary uses **Quantity status** and **Overall status**.
- Project register uses **Status** rather than Position.
- Project editor uses **Current status**.
- Record creation and editing use **Overall status**.
- Supporting and legacy FFC views use Qty/Quantity consistently.

### Detailed Table

The existing Detailed Table structure, columns, inline progress editing, grouping, and Excel export remain unchanged. Its existing **Quantity** and **Status** columns already follow the approved terminology. Only the empty-result wording is standardised.

### PowerPoint briefing export

Newly generated decks now use:

- **TOTAL QUANTITY**
- **QUANTITY STATUS**
- **Country-wise quantity status**
- **IPA and GSL status**
- **OVERALL STATUS**
- **STATUS** as the project-table column
- **Qty** in country, map, project, and appendix references

The map, executive summary, milestone slides, country slides, project-detail slides, operational-focus slides, and appendix are all covered.

## Compatibility approach

Internal CLR types, DTO properties, database fields, JavaScript payload keys, and CSS hooks such as `FfcUnitPosition`, `TotalUnits`, `Position`, and existing `data-ffc-position-*` attributes are intentionally retained. They are implementation contracts rather than user-facing labels. Keeping them avoids an unnecessary schema migration and prevents breaking existing APIs, saved data, and client-side integrations.

## Files added or replaced

See `_FFC_PHASE7A_FILES.txt` for the complete replacement list and `_FFC_PHASE7A_SHA256SUMS.txt` for package checksums.

## Build and test

Run from the project root:

```powershell
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue

npm ci
npm test

dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release
```

No `dotnet ef database update` command is required.

## Manual acceptance checks

1. Open the FFC Portfolio Overview and confirm **Total quantity**, **Quantity status**, **Qty**, and **Current status** are used.
2. Expand a country-year record and confirm the project column is labelled **Status**.
3. Open the Country-Year Workspace and confirm **Quantity status**, **Overall status**, and project **Status** are used.
4. Open Add/Edit Project and confirm the field is labelled **Current status**.
5. Open the Footprint Map and Country Cards and confirm all legends, totals, cards, and detail panels use Qty/Quantity terminology.
6. Generate a new Executive and Full Portfolio PowerPoint and confirm old headings such as `TOTAL UNITS`, `UNIT POSITION`, `OVERALL POSITION`, and project-table `POSITION` are absent.
7. Confirm the Detailed Table still has the same layout, Quantity and Status columns, inline editing, and Excel export.

## Verification completed during preparation

- JavaScript suite: **218 tests passed, 0 failed**.
- JavaScript syntax validation: passed for all changed FFC scripts.
- Production-source terminology scan: no remaining old user-facing FFC labels were found; retained matches are internal identifiers only.
- Focused patch application and package comparison: passed.
- ZIP integrity and SHA-256 package checks: passed.

The preparation environment does not contain the .NET SDK. Therefore, the actual .NET build and .NET test suite must be run on the development computer before deployment.
