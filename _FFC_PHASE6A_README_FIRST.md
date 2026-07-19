# FFC Phase 6A — Footprint Runtime Stabilisation

## Apply this package

This package is designed to be applied **after FFC Phase 6 — Footprint and Presentation Consolidation**.

Extract the ZIP into the `ProjectManagement` project root, retain the directory structure, and replace matching files.

No database migration is required.

## What this correction fixes

The Footprint page failed before rendering because PostgreSQL could not translate this query pattern:

```csharp
.Select(record => new FfcFootprintCountryOption(...))
.Distinct()
.OrderBy(...)
```

The correction queries `FfcCountries` directly and uses an `EXISTS` condition for active, non-archived FFC records. This is naturally distinct and fully relational.

## Additional hardening

The Footprint read service now:

- Uses scalar-only anonymous SQL projections.
- Constructs application DTOs only after materialisation.
- Uses nullable aggregate sums with explicit zero fallback.
- Adds EF query tags for diagnostics.
- Keeps all filtering and unit aggregation in the database.
- Preserves batched project-stage and canonical progress resolution.
- Preserves country options and available years when a filter returns no records.

## Relational regression coverage

`FfcFootprintServiceTests` now runs against an in-memory SQLite relational database rather than the EF Core InMemory provider. This exercises SQL translation for:

- Available years.
- Distinct country options.
- Country/year aggregation.
- Installed, delivered and planned sums.
- Country and linked-project search.
- Archived and inactive-country exclusion.
- Empty filtered results.

The test project already contains `Microsoft.EntityFrameworkCore.Sqlite` from Phase 6.

## Files replaced

```text
Services/Ffc/FfcFootprintService.cs
ProjectManagement.Tests/ProjectOfficeReports/FfcFootprintServiceTests.cs
```

## Protected Detailed Table

The Detailed Table files are not modified by this package.

## Build and test

Run from the project root:

```powershell
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue

dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release
```

No `dotnet ef database update` command is required.

## Manual acceptance checks

1. Open `/ProjectOfficeReports/FFC/Footprint`.
2. Confirm the page renders rather than showing an EF Core translation exception.
3. Verify Map and Country Cards show identical totals.
4. Test year and country filters.
5. Search using an FFC project name and a linked PRISM Project name.
6. Apply a search that returns no countries and confirm filters remain available.
7. Open the legacy `/FFC/Map` and `/FFC/MapBoard` routes and confirm redirection.
8. Confirm the Detailed Table remains unchanged.

## Preparation note

The preparation environment does not contain the .NET SDK, so the actual .NET build and tests must be run on the development computer.
