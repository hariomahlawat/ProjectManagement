# PRISM Proliferation — Visual Alignment Release

## Purpose

This is a cumulative, path-preserved replacement package for the proliferation module. It retains the existing proliferation calculations, approvals, data-quality workflow, reports and permissions while aligning the module with the established PRISM ERP visual language.

The release reduces excessive vertical space, removes dashboard-style visual treatment and improves scan speed across Overview, Project Total, Records, Reports and Manage.

## Replacement procedure

1. Back up the current solution.
2. Extract this archive into the solution root containing `ProjectManagement.csproj`.
3. Allow the files in the archive to overwrite the matching paths.
4. Do not copy the enclosing package folder into the project. The first folders copied into the solution root must be `Areas`, `Services`, `wwwroot`, `ProjectManagement.Tests`, together with `Program.cs`.
5. Clean and rebuild the solution.

Recommended commands:

```powershell
 dotnet clean
 dotnet restore
 dotnet build -c Release
 dotnet test -c Release --no-build
```

Then publish normally:

```powershell
 dotnet publish -c Release -o .\publish
```

## Database impact

No EF Core migration is required. No proliferation business rule, source default or stored total has been changed.

## Principal UI changes

- Uses PRISM-standard page-heading scale, form controls, borders, radii and card depth.
- Removes the redundant proliferation eyebrow from normal module pages.
- Compacts the module navigation, page headers, warnings, project finder and KPI cards.
- Replaces the large blue project hero with a standard PRISM summary card.
- Reduces collapsed project-year rows to a compact single-line disclosure pattern.
- Moves the year disclosure chevron to the right and removes the large unused row area.
- Simplifies source calculations while retaining full auditability and calculation transparency.
- Reduces project table, record group and manager list row heights.
- Compacts report tiles, filters, workspaces and management controls.
- Reduces chart height, normalises chart typography and displays values on technical-category bars.
- Retains responsive layouts, keyboard project pickers and lazy loading of detailed entries.

## Key verification checklist

After replacement, verify the following:

1. Overview shows the project search, four KPI cards and the start of Project totals within a substantially shorter vertical area.
2. The data-quality warning remains visible but uses a compact operational alert.
3. Project Total uses a white summary card with a blue total, not a full-width blue banner.
4. A collapsed year row displays year, SDD, 515 ABW, reported total and chevron on one line.
5. Opening a year shows compact calculations and still loads detailed entries only on demand.
6. Records filters and grouped calculations remain functional.
7. Reports still collapse the report chooser after a report is selected.
8. Manage workspaces, approval, counting-rule and data-quality actions remain functional.
9. Year-wise and technical-category charts render correctly.
10. Technical-category bars display exact values at the end of the bars.

## Validation performed in this environment

- All nine packaged JavaScript files passed `node --check`.
- `proliferation.css` parsed successfully with `tinycss2` and contains balanced rule blocks.
- All local CSS and JavaScript references in the packaged Razor files resolved in an assembled copy of the repository.
- Visual-alignment hooks and compact project/calculation markup were verified.

The .NET SDK was not available in this environment. The complete .NET build and test suite must therefore be run on the development machine before release. The full Node test suite also could not be completed because `jsdom` was not installed and the offline npm cache was incomplete.

## Files

`REPLACEMENT-MANIFEST.txt` lists every cumulative replacement path. `SHA256SUMS.txt` contains checksums for integrity verification. `STATIC-VALIDATION.txt` records the checks completed here.
