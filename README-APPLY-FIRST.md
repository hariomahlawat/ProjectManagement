# PRISM ARPP/PPP Fresh Experience Redesign

This package replaces the accumulated ARPP UI patches with a coherent, task-oriented presentation layer.

## Scope

- Unified ARPP navigation: **Published / Administration / Reconciliation**.
- Rebuilt administration register with precise working and published states.
- Rebuilt record reader with **Overview / Issued rows / HQ document / Audit** tabs.
- Dedicated full-viewport document workspace with one controlled grid scroller.
- Row navigator for all rows, issues, unlinked rows and delisted rows.
- Compact issue navigation, reliable dirty state and disabled Save while clean.
- Responsive read-only cards and mobile row editing presentation.
- Refined create, reconciliation and project-history pages.

## Explicitly not included

- No database migration.
- No entity or schema change.
- No ARPP publication-rule change.
- No IPA resolver or stage-synchronisation change.
- No service or repository replacement.

## Apply by replacing files

Copy the contents of this folder into the project root and allow the listed files to replace the existing files. Use `REPLACEMENT-MANIFEST.txt` as the authoritative file list.

PowerShell example from this package folder:

```powershell
robocopy . "E:\Dot Net Web Development\ProjectManagement" /E /XF README-APPLY-FIRST.md REPLACEMENT-MANIFEST.txt IMPLEMENTATION.patch VALIDATION.txt SHA256SUMS.txt
```

## Apply as a patch

From the project root, copy `IMPLEMENTATION.patch` there and run:

```bash
patch -p1 < IMPLEMENTATION.patch
```

## Build and test

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

After deployment, perform a hard refresh because the ARPP CSS and JavaScript are versioned static assets.

## Manual acceptance checks

1. Published page shows unified module navigation and retains the document rail.
2. Administration records clearly distinguish verified, unlocked, incomplete and ready states.
3. Record page tabs switch without page reload and no table heading overlaps any row.
4. Edit working copy opens a full-viewport workspace with no PRISM page scrolling behind it.
5. Only the grid scrolls; its heading remains fixed inside that grid.
6. Save is disabled when clean and enabled after a genuine edit.
7. Issues opens the row navigator and focuses the selected invalid control.
8. Row filters correctly show all, issue, unlinked and delisted rows.
9. Document details opens in the right drawer.
10. Mobile/tablet layouts show readable record cards rather than a compressed desktop table.
