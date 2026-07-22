# FFC Phase 3 — Unified Country-Year Record Workspace

## Apply this package when

Phase 1 and Phase 2 have already been applied. Extract this folder into the **ProjectManagement project root**, preserve the directory structure, and replace matching files.

A cumulative Phase 1 + Phase 2 + Phase 3 package is supplied separately for installations that have not yet applied the earlier phases.

## Main implementation

- Adds a unified country-year workspace containing record summary, projects and attachments.
- Adds focused country-year record creation with an empty country selector and current-year default.
- Adds protected record, project and attachment editors with explicit Save/Cancel behaviour and unsaved-change protection.
- Adds a searchable PRISM Project picker and preserves linked Project external remarks as the canonical FFC progress source.
- Replaces independent delivery/installation checkboxes with Planned, Delivered and Installed progression.
- Adds project-level optimistic concurrency and the corresponding EF Core migration.
- Adds safer attachment storage, checksum-based duplicate detection, user attribution, recoverable deletion and PDF-ingestion warning handling.
- Redirects legacy record/project/attachment GET routes into the new workspace while retaining legacy POST handlers for compatibility.
- Adds archived-record administration and an explicit restore path.
- Refines the Portfolio Overview: workspace links, consolidated actions, compact advanced filters and omission of empty milestone-remarks space.

## Protected Detailed Table

The following files are intentionally unchanged:

- `Areas/ProjectOfficeReports/Pages/FFC/MapTableDetailed.cshtml`
- `Areas/ProjectOfficeReports/Pages/FFC/_DetailedTablePartial.cshtml`
- `wwwroot/js/pages/project-office-reports/ffc/ffc-map-table-detailed-inline-edit.js`

Their verified SHA-256 values are:

```text
513d9173fa3593cc6cdbf00e0ef781fb3ce70d0ddc7b3055d9ce44644f2f32a8  MapTableDetailed.cshtml
d977daadc4b15ac33d43b279fee11bc397e4654d1fc4aca67c8fde39ca556e32  _DetailedTablePartial.cshtml
37150acba397898bf73e1cff453aaad2f8fae1b8fe3a1c7d77fe7294b516e20a  ffc-map-table-detailed-inline-edit.js
```

## Database change

This phase adds migration:

```text
20261201270000_AddFfcProjectConcurrency
```

It adds and backfills `FfcProjects.RowVersion` for optimistic concurrency.

## Build and deployment sequence

From the ProjectManagement project root:

```powershell
dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release
dotnet ef database update
```

If the application performs migrations automatically at startup, still build and test before deployment, then confirm the new migration is recorded in `__EFMigrationsHistory` after startup.

## Verification completed in the preparation environment

- JavaScript syntax checks passed.
- Project XML files parsed successfully.
- C# delimiter and source-integrity scans passed.
- Patch whitespace validation passed.
- Protected Detailed Table file hashes match the Phase 2 baseline.

The preparation environment does not contain the .NET SDK, so `dotnet build`, `dotnet test` and live migration execution were not run here. Run all three before deployment.
