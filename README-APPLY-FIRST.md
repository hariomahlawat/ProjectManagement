# PRISM ARPP/PPP Workspace Final Polish

This package is a focused UI/UX finishing pass for the latest ARPP working-copy workspace.
It must be applied **after** the ARPP Workspace Defect Stabilisation phase currently installed in the project.

## Scope

The package refines the existing workspace without changing its architecture or business behaviour:

- The **Rows** control now communicates whether the row navigator is visible and what clicking the control will do.
- Horizontal-scroll edge cues are shown only when applicable.
- The fixed Actions strip uses a restrained contextual boundary shadow and visually remains part of each row.
- **Save working copy** is unmistakably disabled while the form is clean.
- The validation tray no longer shows a redundant **Go to first issue** action for short, already-visible issue lists.
- Validation actions are hidden entirely when they add no value.
- Entry guidance always opens in the collapsed state for an uncluttered returning-user experience.
- UI contract tests cover the refined interaction behaviour.

## Replacement method

Copy the four project-relative files from this folder into the project root and replace the existing files:

1. `Areas/ProjectOfficeReports/Pages/ARPP/Manage.cshtml`
2. `wwwroot/css/project-office-reports/arpp-workspace.css`
3. `wwwroot/js/pages/project-office-reports/arpp/arpp-manage.js`
4. `ProjectManagement.Tests/Arpp/ArppWorkspaceDefectStabilizationContractTests.cs`

The supplied `IMPLEMENTATION.patch` is an alternative to manual replacement. It is generated against the latest ARPP Workspace Defect Stabilisation baseline.

## Database and backend impact

- No migration
- No entity-model change
- No service change
- No publication-rule change
- No IPA resolver or stage-synchronisation change

## Recommended local validation

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

Then verify in a browser:

- Rows navigator expanded and collapsed
- Search at the far-left and far-right grid positions
- Issues tray with 1–5 issues and with more than 5 issues
- Issues tray while a non-issue filter is active
- Disabled and enabled Save states
- Actions column while horizontally scrolling
- Entry guidance after page reload
- 1366×768 at 125% Windows scaling
