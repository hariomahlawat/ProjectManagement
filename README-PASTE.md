# PRISM Compendium Phase 46.2 — Physical Layout Safety Closure

## Purpose

This is a focused hotfix for the production `DocumentLayoutException` that can occur when a Compendium dossier is accepted by the SkiaSharp pagination planner but QuestPDF subsequently finds the atomic `ShowEntire()` dossier slightly taller than the real A4 envelope. The reported projects work in Balanced mode because Balanced overlaps image and narrative vertically; stacked layouts (Visual / Multi-image / Technical) consume image + narrative height additively and expose the cross-engine boundary more readily.

## Root cause fixed

The previous physical shaping reserve was a hard-coded **12 pt** while the maximum Compendium body line is:

`10 pt × 1.10 max narrative scale × 1.25 line height = 13.75 pt`

The old reserve was therefore smaller than the line it claimed to protect. Phase 46.2 centralises the body typography metrics and derives the reserve as:

`13.75 pt maximum body line + 2.25 pt native shaping tolerance = 16.00 pt`

This reduces planner-usable first-page height by only **4 pt** compared with the previous build, but gives QuestPDF a full maximum-scale body line plus native shaping/rounding tolerance before the atomic `ShowEntire()` guard can be reached.

## Production files to replace

Replace these three files in the application project:

1. `Utilities/Reporting/CompendiumLayoutMetrics.cs`
2. `Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs`
3. `Utilities/Reporting/CompendiumBuildIdentity.cs`

The remaining files in this bundle are regression tests and should also be copied when you keep the repository test suite in sync.

## Behaviour intentionally preserved

- `ShowEntire()` remains in place. Do **not** remove it; it protects index/page-number integrity.
- Balanced / Flow-below-image logic is unchanged.
- Justification is unchanged.
- Image Fill/Fit geometry is unchanged.
- Page-layout selection/scoring is unchanged.
- Review fingerprints are unchanged.
- No EF migration or database update is required.

If a stacked dossier is too close to the physical boundary after this change, the planner has 4 additional points of protected safety margin and will prefer a smaller one-page candidate or controlled continuation instead of allowing QuestPDF to discover the overflow after planning.

## Build identity

Phase: `46.2`

Build stamp: `CompendiumPdf_2026-08-25_phase46.2-physical-layout-safety`

PDF contract: `physical-a4-v46.2`

This makes it possible to confirm from diagnostics / the Compendium response header that the corrected build is actually deployed.

## Local verification on your development machine

From the solution/project root run:

```powershell
dotnet clean
dotnet build
dotnet test
```

Then test Preview PDF with the same projects that previously failed in non-Balanced layouts, especially:

- `Inf Wpn Trg Sml IWTS (Wireless) Ver-2 (Wireless comn)`
- `VR Based Map Reading (VR MaRS) Sml`

Test the previously failing layout, not only Balanced. If the dossier no longer fits safely on one page, an intentional continuation/page-plan change is acceptable; an unplanned QuestPDF page is not.

## Deployment

No migration is included. Copy/publish the application normally. On the IIS machine ensure the deployed build identity is Phase 46.2 before retesting.

## Verification limitation in this package environment

The execution environment used to prepare this bundle does not contain the .NET SDK, so `dotnet build` and xUnit could not be executed here. The JavaScript/static publication contract suite and package integrity checks were executed; see `VERIFICATION.txt`.
