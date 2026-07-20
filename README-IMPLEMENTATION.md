# FFC dashboard module navigation refinement

Apply this bundle on top of the current **FFC dashboard widget professional refinement**.

## Replace these files

- `Pages/Dashboard/Index.cshtml`
- `wwwroot/css/pages/dashboard.css`
- `ProjectManagement.Tests/DashboardFfcWidgetContractTests.cs`

## Implemented

- Retains **Partner countries** without changing its calculation or label.
- Replaces the single **View full map** header action with the primary **Open FFC portfolio** link.
- Adds a compact overflow menu containing:
  - **Full map**
  - **Detailed table**
- Uses the existing FFC routes:
  - `/ProjectOfficeReports/FFC/Index`
  - `/ProjectOfficeReports/FFC/Map`
  - `/ProjectOfficeReports/FFC/MapTableDetailed`
- Keeps administrative actions out of the dashboard card.
- Shortens the completed-unit breakdown to **awaiting installation** while retaining the full definition in the information tooltip.
- Adds keyboard focus, hover, expanded-state and responsive styling for the navigation controls.
- Ensures the dropdown is layered above the interactive map.
- Adds contract-test coverage for the new navigation structure.

## Verification

After replacing the files:

```powershell
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Then reload the dashboard with `Ctrl+F5` so the versioned CSS is refreshed.

No database migration, service registration, JavaScript or package change is required.
