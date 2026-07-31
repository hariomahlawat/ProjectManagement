# PRISM ARPP/PPP — Published Position Semantics

## Purpose

This focused patch corrects the Administration register so that each financial-year heading displays the **authoritative published position** from verified ARPP snapshots. Editable working-copy values remain visible only against their individual records and are no longer mixed into the financial-year headline.

## What changes

- Financial-year summaries use `IArppLibraryService.GetCurrentPositionAsync`, the same published source used by the organisation-wide ARPP library.
- The published summary is intentionally independent of free-text administration filters, so a working or historical row cannot become the apparent authoritative position.
- Working-copy and published values remain clearly separated at record level.
- Empty financial years show **No published position** instead of authoritative-looking zero totals.
- The header shows precise scope wording such as **3 structured rows across 2 records**.
- Unlinked published rows and records under correction are surfaced as secondary context.
- The top KPI now states the number of structured rows and records explicitly.

## Apply

Copy the files in this package to the same relative locations in the project, replacing the three existing files and adding the test file.

Then run:

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

## Database and business logic

- No migration is required.
- No entity or schema is changed.
- No verification, publication, reconciliation or IPA-stage rule is changed.
- The patch reuses the existing published-library service as the authoritative source.

## Expected UI

For a financial year with published data:

- `3 structured rows across 2 records`
- `Published position`
- authoritative approved and delisted totals
- optional notes such as `1 record under correction`

For a financial year without a verified published snapshot:

- `No published position`
- the number of records currently under work
