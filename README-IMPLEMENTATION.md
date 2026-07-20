# FFC Dashboard Widget — Professional Refinement

Replace/add the files in this bundle relative to the folder containing `ProjectManagement.csproj`.

## Replacement files

- `Pages/Dashboard/Index.cshtml`
- `Areas/Dashboard/Components/FfcSimulatorMap/_Widget.cshtml`
- `wwwroot/css/pages/dashboard.css`
- `wwwroot/css/widgets/ffc-simulator-map.css`
- `wwwroot/js/widgets/ffc-simulator-map.js`

## New test file

- `ProjectManagement.Tests/DashboardFfcWidgetContractTests.cs`

## Implemented behaviour

- Reduces the dashboard card height while retaining a readable operational overview.
- Automatically fits the map to active partner-country label points rather than the full world extent.
- Uses Natural Earth label coordinates where available, with robust ISO fallbacks including `ADM0_A3`.
- Resolves overlapping markers through deterministic deconfliction and subtle leader lines.
- Shows completed units in the principal marker value.
- Shows additional planned units as an amber badge on mixed completed/planned locations.
- Uses an amber marker value for planned-only countries.
- Clarifies metrics as Partner countries, Completed units and Planned units.
- Shows the completed breakdown as installed and delivered/awaiting installation.
- Qualifies the ranking as Top countries by completed units.
- Makes map countries, markers, tooltips and ranked country rows open the country-filtered detailed table.
- Adds keyboard focus and Enter/Space activation for map markers.
- Preserves normal link navigation for ranked country rows; JavaScript no longer hijacks their click.
- Uses edge-aware interactive tooltips with a direct View FFC projects action.
- Improves country-border contrast while retaining a restrained dashboard visual hierarchy.
- Includes responsive behaviour for desktop, tablet and mobile layouts.

## Scope

- No database migration.
- No package change.
- No service-registration change.
- Existing FFC roll-up business logic is retained.

## Verification

After replacement, run:

```powershell
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Then hard-refresh the dashboard with `Ctrl+F5` so the revised CSS and JavaScript are loaded.
