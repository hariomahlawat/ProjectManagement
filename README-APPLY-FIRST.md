# PRISM ARPP/PPP — Final UI/UX Completion

This package is designed for the current ARPP/PPP codebase after the earlier Production UX and UI/UX Stabilisation phases.

## Scope

This is a focused UI/UX completion phase. It introduces **no database migration**, no entity/table change, no publication-rule change and no IPA-stage synchronisation change.

Two read-service files are included only to expose existing publication and reference-mapping state to the register UI. They do not write data or alter authoritative calculations.

## Improvements included

- One vertical scroll surface in the normal document workspace.
- Full-screen mode retains one controlled workspace scroll surface.
- Compact, opt-in **Review issues** tray instead of a permanent large validation band.
- Save remains disabled until the user makes a real change.
- Clear clean, dirty, saving and validation states.
- Responsive command bar that keeps Add row and Save visible.
- Compact Details-page issued-row table with a wider project column.
- Issued and linked project names are not repeated when they are the same.
- Sticky Details table headings for long ARPP documents.
- Duplicate Details-page Edit rows action removed.
- Register headline changed from “Issued documents” to “ARPP records”.
- Precise register states: Verified and published, Unlocked for correction, Setup incomplete, Working copy, Reference review required and Ready for verification.
- Published-revision continuity is shown for an unlocked correction.
- UI contract tests updated and extended.

## Apply

Copy the contents of this folder into the project root, preserving the relative paths and replacing the listed files.

The authoritative replacement list is in `REPLACEMENT-MANIFEST.txt`.

## Database

Do **not** create or apply a migration for this package.

## Build and test

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

## Recommended visual checks

1. Open an incomplete working copy. Confirm Save is disabled and the status says “No unsaved changes”.
2. Open Review issues, jump to an error, and confirm the field is fully visible below the sticky toolbar.
3. Scroll through all rows in normal mode. Confirm there is only one vertical scrollbar.
4. Enter and exit full-screen mode. Confirm the position is preserved and only the workspace scrolls.
5. Edit a field. Confirm Save becomes enabled; revert the field and confirm it disables again.
6. Review Details at 1366×768 and 125% scaling. Confirm the project column is compact and headings remain visible.
7. Confirm the register shows “Unlocked for correction” for a previously published unlocked record and “Setup incomplete” for an empty record.

## Rollback

Restore the ten files listed in `REPLACEMENT-MANIFEST.txt` from source control or your pre-deployment backup. No database rollback is required.
