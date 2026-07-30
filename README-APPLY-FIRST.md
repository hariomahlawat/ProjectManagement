# PRISM ARPP/PPP — UI/UX Production Stabilisation

This is a focused **UI/UX-only replacement package** for the ARPP/PPP module. It is intended to be applied on top of the preceding ARPP Production UX phase/current implementation shown in the reviewed screenshots.

## Scope implemented

- Separates validation state from actual unsaved changes. An incomplete loaded record now shows **No unsaved changes** together with the number of fields requiring attention; it does not falsely activate Save or trigger a navigation warning.
- Uses a canonical form snapshot so editing and then restoring the original values returns the workspace to a clean state.
- Rebuilds the sticky workspace stack around a contained table viewport. The command bar is opaque, the table heading remains above row 1, and content no longer shows through the toolbar.
- Keeps the upper and lower horizontal scroll positions synchronized and retains left/right overflow cues.
- Refines the command bar for wide and constrained monitors. Less-frequent functions move into **Entry tools** at smaller widths while Add row and Save remain visible.
- Renames **Cancel** to **Discard and return** and adds a deliberate confirmation only when real unsaved changes exist.
- Groups validation issues by row/section, initially limits the list to five groups, and provides **View all / Show fewer** and direct field focus.
- Remembers the entry-guidance state after the user dismisses it.
- Consolidates publication continuity and verification readiness into one authoritative document-state panel on Details.
- Reduces Details to four KPI cards, moves issued rows above PDF management, and converts the attachment area into a compact evidence strip with expandable management controls.
- Clarifies **working** versus **published** values and administrative actions in the register.
- Shortens and clarifies ARPP library search.
- Adds source-contract tests for the stabilised behaviours.

## Explicitly not changed

- No EF Core migration.
- No database schema or entity change.
- No service, resolver, IPA synchronisation or publication-history change.
- No permission or workflow-rule change.

## Apply

1. Back up the current project or create a source-control checkpoint.
2. Open the folder `PRISM_ARPP_UI_UX_Production_Stabilisation` from this archive.
3. Copy its contents into the project root containing `ProjectManagement.csproj`.
4. Replace the eight listed source files and add the one test file.
5. Clear the browser cache after deployment. CSS and JavaScript references already use `asp-append-version`.

## Build and test

Run from the project root:

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

No migration command is required.

## Browser acceptance check

Test at 100%, 125% and 150% Windows display scaling, with particular attention to 1366×768 at 125%:

1. Open an incomplete working copy without editing it. Confirm the status reads **No unsaved changes · N fields require attention** and Save remains disabled.
2. Change a value and restore the original value. Confirm the workspace returns to **No unsaved changes**.
3. Scroll the page and the row viewport. Confirm the command bar is opaque and the table heading always remains before row 1.
4. Confirm the upper horizontal scrollbar and table scroll remain synchronized.
5. Confirm **Entry tools** appears on constrained widths and Add row / Save remain accessible.
6. Select **Discard and return** while clean, then while dirty. Only the dirty state should ask for confirmation.
7. Create more than five validation groups and verify **View all / Show fewer** and direct focus.
8. Review Details. Confirm one state panel, four KPIs, issued rows before the PDF area, and compact PDF management.
9. Review the register and confirm working values are not presented as published values.
10. Check the published ARPP library search and both desktop and mobile navigation.

## Rollback

Restore the previous versions of the files in `REPLACEMENT-MANIFEST.txt` and remove the added contract-test file. No database rollback is required.
