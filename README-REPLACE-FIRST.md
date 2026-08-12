# PRISM Publications — Phase 13
## Deterministic Print Composition Lock

Phase 13 is an incremental replacement over the Phase 12 Brochure Builder implementation.

The acceptance standard is unchanged: **Print / Compact output must be equal to or better than the original reference brochure** while remaining deterministic, editable from PRISM data and safe for offline deployment.

## What Phase 13 fixes

1. **Planner and renderer now share one exact module-height contract.**
   - QuestPDF consumes `BrochurePrintProjectMeasurement.TotalHeightPoints` as a fixed module height.
   - The old `MinHeight(...)` path is removed.
   - Sheet-level project and closing gaps are explicit measured spacer items; no hidden `Column.Spacing(...)` can inflate the rendered page.
   - Closing-panel width/padding and top/bottom geometry are measured with the same constants that are rendered.
   - A planner measurement can no longer reserve invisible height that the rendered card does not occupy.

2. **9 pt project body is a hard normal-print constraint.**
   - Visual and Balanced remain 9 pt.
   - Compact 8.5 pt is exposed only when an individual project cannot fit on a complete sheet at 9 pt.
   - Compact cannot be selected merely to squeeze another project or closing matter onto a page.

3. **Page count is minimised after quality constraints are satisfied.**
   - Among equal-page-count layouts, the planner minimises the worst residual/dead-tail space and then aggregate composition cost.
   - A regression test reproduces the measured Phase 12 three-project combination that was incorrectly split across sheets.

4. **Project imagery is bounded to reference scale.**
   - 16:9 remains canonical.
   - Normal image expansion is capped at 160 pt rather than the previous 176 pt.
   - Residual enlargement is limited to 12 pt.

5. **Compact paragraph rhythm is explicit and measured.**
   - Blank-line paragraph gaps are no longer treated as a full 9 pt text line; repeated blank lines do not consume additional measured height.
   - Project paragraph spacing is 2.25 pt and is shared by Skia measurement and QuestPDF composition.

6. **Float continuation semantics are explicit.**
   - Paragraph / Sentence / Word split type is carried in the layout contract.
   - Forced mid-sentence continuations remain left aligned and use a minimal continuation gap.
   - Semantic paragraph boundaries receive the appropriate compact paragraph gap.

7. **Title band density is tightened.**
   - Single-line title-band floor is reduced from 20 pt to 18 pt.
   - Long titles still grow vertically rather than collapsing typography.

8. **Generated institutional cover alternatives use exact PRISM identity overlays.**
   - `Reference Original` is left untouched.
   - Generated alternatives receive the deployed `img/logos/artrac.png` and `img/logos/sdd.png` assets as formal identity overlays.

9. **Closing treatment is tightened without introducing a new font dependency.**
   - Visionary copy is slightly more prominent and compact.
   - The existing registered offline publication font remains authoritative, avoiding a fragile server-OS serif-font dependency.

## Files to replace

Copy these Phase 13 files to the same relative locations in the project:

- `Services/Publications/BrochureContracts.cs`
- `Services/Publications/BrochurePrintLayoutMetrics.cs`
- `Services/Publications/BrochurePrintMeasurementService.cs`
- `Services/Publications/BrochurePrintPagePlanner.cs`
- `Utilities/Reporting/BrochurePrintCompactComposer.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintMeasurementServiceTests.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintCompactPlannerTests.cs`
- `wwwroot/js/projects/publications-brochure-contract.test.js`
- `tools/Test-PrismPublicationsPhase13.ps1`

The incremental package also carries the five Phase 12 Cover A image assets so it is self-contained. They can safely overwrite the existing copies.

## Installation

From the project root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase13.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

## Mandatory acceptance run

Regenerate the **same nine-project Print / Compact test brochure** used for the Phase 12 review.

Check the actual PDF, not only the preflight estimates:

- Page 2 should no longer show the false two-project break seen in Phase 12 when three measured projects fit.
- Normal project body copy should remain 9 pt.
- Project images should generally sit near the original reference scale and never exceed 160 pt in normal residual expansion.
- Mid-sentence wrap continuations should no longer look like newly justified paragraphs.
- Project title bands should remain compact for single-line titles and expand cleanly for long titles.
- Gallery 2 must retain right-hand stacked 16:9 imagery.
- Final closing matter should share the last project sheet whenever it genuinely fits without compromising normal project typography.
- There must be no clipping, overflow, orphan title bands or project-order changes.

## Important

Phase 13 intentionally does **not** redesign Digital / Comfortable. All changes in this phase are isolated to Print / Compact composition and its regression contracts.
