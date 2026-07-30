# PRISM ARPP/PPP — Production UX Phase

This package implements the next production-focused user-experience phase for the ARPP/PPP module. It is a **source-file replacement package** and contains no database migration.

## Scope implemented

- Corrects the global page-shell width defect so `wide`, `calendar`, `analytics` and related shell variants use their intended actual width.
- Moves the ARPP document editor to the unrestricted workspace shell.
- Adds a persistent document command bar with row count, jump search, bulk actions, full-screen mode, save state and keyboard shortcuts.
- Reduces frozen-table width by combining Serial No., PPP No. and issued project reference into one structured **Issued identity** column while preserving all separate bound model fields.
- Adds a synchronized upper horizontal scrollbar and left/right overflow cues.
- Calculates sticky offsets from the live PRISM top bar and module navigation.
- Adds a row-aware validation navigator that takes the user directly to invalid fields.
- Adds `Ctrl+S` / `Cmd+S` to save, `Alt+A` to add a row and `Esc` to exit full-screen mode.
- Improves unsaved/saving state wording and preserves the existing explicit-save and concurrency model.
- Improves unlock-reason validation with a live character counter, controlled inline error state and no browser validation bubble.
- Standardises key action wording on the Details page.
- Adds source-contract tests for the production UX requirements.

## Apply

1. Back up the current source tree or create a source-control checkpoint.
2. Extract this archive into the **ProjectManagement project root**—the folder containing `ProjectManagement.csproj`.
3. Allow the eight listed files to replace or add files at the same relative paths.
4. Clear browser cache after deployment because CSS and JavaScript files changed. The Razor pages already use `asp-append-version`.

## Build and test

Run from the project root:

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

No EF Core migration is required for this phase.

## Browser acceptance check

Test at 100%, 125% and 150% Windows display scaling:

1. Open an unlocked ARPP issue and select **Edit issued rows**.
2. Confirm the workspace uses the full available browser width.
3. Confirm the upper horizontal scrollbar appears whenever the table overflows and remains synchronized with the table.
4. Confirm only the row number and Issued identity remain frozen on the left, with Actions frozen on the right.
5. Search by Serial No., PPP No., project reference or linked PRISM project in **Jump to row or project**.
6. Enter invalid data, select **Save issue**, and confirm the validation navigator opens and focuses the selected field.
7. Test **Full screen**, then exit using the button and `Esc`.
8. Test `Ctrl+S` and `Alt+A`.
9. Open the unlock dialog and confirm the action remains disabled until a clear 10-character reason is entered.
10. Verify normal save, concurrency warning, paste-row preview and project linking still operate as before.

## Rollback

Restore the previous versions of the files listed in `REPLACEMENT-MANIFEST.txt`. No database rollback is necessary.
