# PRISM ARPP/PPP — Visual System and Density Completion

This package is designed for the latest **ARPP Fresh Experience Redesign + Production Finishing** implementation.

## Scope

The phase standardises the complete ARPP/PPP front end without changing the database, entities, publication workflow, reconciliation rules, IPA resolver or stage synchronisation.

Implemented improvements include:

- Shared light/dark ARPP design-token layer.
- Clear cool-grey canvas, white primary surfaces, stronger borders and readable muted text.
- Compact module navigation and page headers.
- White Administration KPI, filter, financial-year and record surfaces.
- Denser Record page with compact state panel and tabs.
- Removal of repeated tab-section headings; data begins higher in the viewport.
- Sharper read-only tables with clear header and row separation.
- Workspace white rows, stronger input boundaries, focus rings and restrained validation emphasis.
- Distinct navigator and grid surfaces.
- Horizontal overflow edge cues and refined fixed Actions strip.
- Consistent visual treatment for Published, Create, Reconciliation and Project History pages.
- Print/PDF working-versus-published labels, em dashes for absent identifiers and working-copy completeness disclosure.
- UI contract tests for the token layer, density, workspace and print behaviour.

## Apply by replacement

1. Back up the project.
2. Copy the contents of this folder into the project root.
3. Replace existing files when prompted.
4. Confirm that the new file `wwwroot/css/project-office-reports/arpp-tokens.css` is copied.
5. Build and test in Release configuration.

PowerShell example from inside this package folder:

```powershell
Copy-Item .\Areas -Destination <PROJECT_ROOT> -Recurse -Force
Copy-Item .\Pages -Destination <PROJECT_ROOT> -Recurse -Force
Copy-Item .\ProjectManagement.Tests -Destination <PROJECT_ROOT> -Recurse -Force
Copy-Item .\wwwroot -Destination <PROJECT_ROOT> -Recurse -Force
```

## Build and test

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

## Browser checks

Review these pages after deployment:

- Published current position and issued document.
- Administration top and lower financial-year sections.
- Verified and unlocked Record pages: Overview, Issued rows, HQ document and Audit.
- Workspace: All rows, Needs attention, Delisted, navigator expanded/collapsed and horizontal scrolling.
- Reconciliation and Project History.
- Print/PDF for verified and unlocked records.

Test at 1366×768 with Windows scaling at 125%, 1536×864 and 1920×1080.

## Database

No migration is included or required.
