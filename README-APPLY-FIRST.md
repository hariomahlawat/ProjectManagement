# PRISM ARPP/PPP Workspace Defect Stabilisation

This is a focused replacement package for the latest ARPP/PPP visual-system baseline.

## Scope

The package fixes only the confirmed working-copy workspace defects:

1. Collapsing **Rows** now hides only the row navigator; the editor grid remains mounted and expands to the full workspace width.
2. Validation field totals, affected-row totals, navigator filters and invalid-row accents are derived from one authoritative validation result.
3. Adding a row refreshes validation, scrolls the new row fully into view and focuses **Serial No.** without an uncontrolled browser jump.
4. Every row containing a validation issue receives the same restrained left-edge indication; the selected issue remains more prominent.
5. The **Rows** toggle exposes an explicit, state-aware accessible action: **Show row navigator** or **Hide row navigator**.
6. The toolbar issue badge reports affected rows, matching the **Needs attention** navigator count; the issue tray continues to show the precise field total.

## No backend change

- No migration
- No entity/model change
- No service or resolver change
- No publication-rule change
- No IPA-stage synchronisation change

## Files to replace/add

Copy the package contents into the project root, preserving paths:

- `Areas/ProjectOfficeReports/Pages/ARPP/Manage.cshtml`
- `wwwroot/css/project-office-reports/arpp-workspace.css`
- `wwwroot/js/pages/project-office-reports/arpp/arpp-manage.js`
- `ProjectManagement.Tests/Arpp/ArppWorkspaceDefectStabilizationContractTests.cs`

## Local verification

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

Then verify in the browser:

1. Open an ARPP working copy.
2. Select **Rows**; confirm only the navigator collapses and the grid expands.
3. Select **Rows** again; confirm the navigator returns without losing scroll position.
4. Select **Add row**; confirm the new row is fully visible and Serial No. receives focus.
5. Confirm the toolbar issue badge and **Needs attention** count both increase to the number of affected rows.
6. Enter the missing fields and confirm both counts reduce together.
7. Change a row to **Delisted** and confirm Serial/PPP validation clears immediately.
8. Duplicate/delete/link rows and confirm counts remain current.
