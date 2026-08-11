# PRISM Publications — Phase 12 Output-Verified Reference Quality

## Objective

Phase 12 moves Print / Compact from "close to the original" to a renderer explicitly engineered
for **equal-or-better reference quality**. It keeps the Phase 10/11 measured architecture and fixes
the remaining defects observed in the generated PDF while adding a curated institutional Cover A
artwork library.

Digital / Comfortable remains isolated except that, when Institutional Cover A is selected, it may
reuse the same user-selected institutional artwork.

## What Phase 12 changes

### 1. Curated Cover A artwork library

Cover A now ships with five fully offline hero artworks and exposes them directly in Brochure
Builder:

1. **Reference Original** — default and closest to the approved brochure;
2. **Premium Green–Gold**;
3. **Cinematic Cyber**;
4. **Executive Teal**;
5. **Luminous Halo**.

All shipped artwork is normalized to **1600 × 1280 (5:4)** for deterministic print composition.
The selected artwork is stored in the posted brochure options; no database migration is required.

The selected artwork is treated as full institutional hero artwork, so PRISM does **not** paint a
second logo/title lockup over it. The authoritative Centre of Expertise statement remains live PDF
text and is overlaid in the lower safe zone of the hero.

### 2. Cover A first-page integrity

- `CONTACTS` now owns a dedicated row above the agency headings;
- Developing and Manufacturing Agency headings can no longer collide with the badge;
- agency columns are deliberately asymmetric (**61 / 39**) because Developing Agency carries
  more copy;
- the Centre of Expertise statement is integrated into the artwork hero rather than consuming a
  separate full-width band;
- the recovered vertical space supports stronger hero presence and more comfortable copy.

### 3. Reference-scale project imagery

Print / Compact retains exact **16:9** project imagery and raises the normal image scale:

- Visual: approximately **154 pt** before bounded narrative adjustment;
- Balanced: approximately **148 pt**;
- Compact emergency layout: approximately **136 pt**;
- residual expansion remains bounded at **176 pt** maximum.

This brings the generated visual anchor into the same range as the original brochure while keeping
PRISM's cleaner standardized 16:9 treatment.

### 4. 9 pt is now the real normal-body floor

Visual and Balanced remain **9 pt**. Compact at 8.5 pt is no longer offered to ordinary multi-
project packing. It is available only as an emergency escape hatch for a single oversized project
that cannot physically fit at 9 pt.

This means page count can no longer silently buy density by reducing ordinary project text.

### 5. Mid-sentence float continuation is corrected

When the image-height split has to occur in the middle of a sentence, Phase 12 separates the
unfinished sentence from normal paragraph copy:

- the unfinished sentence continues full-width **without forced justification**;
- normal justified publication copy resumes at the next sentence boundary.

This removes the visibly stretched first continuation line that exposed the algorithmic split.

### 6. Under-filled pages use residual space deliberately

Residual optimisation now has three bounded stages without changing project order, page membership
or typography:

1. enlarge project imagery;
2. add measured vertical breathing inside project modules;
3. add modest inter-module spacing.

The target remains approximately **95% physical sheet utilisation** rather than artificial 100%
fill.

### 7. Closing page is cleaner

The final closing block now ends on **New Simulators**. The institutional strapline remains on the
first page only, matching the reference logic and avoiding a redundant final-page repeat.

## Files changed

- `Pages/Projects/Publications/Brochure/Index.cshtml`
- `Pages/Projects/Publications/Brochure/Index.cshtml.cs`
- `Services/Publications/BrochureContracts.cs`
- `Services/Publications/BrochurePrintLayoutMetrics.cs`
- `Services/Publications/BrochurePrintMeasurementService.cs`
- `Services/Publications/BrochurePrintPagePlanner.cs`
- `Utilities/Reporting/BrochurePdfReportBuilder.cs`
- `Utilities/Reporting/BrochurePrintCompactComposer.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintMeasurementServiceTests.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintCompactPlannerTests.cs`
- `wwwroot/css/pages/projects-publications.css`
- `wwwroot/js/pages/projects-brochure.js`
- `wwwroot/js/projects/publications-brochure-contract.test.js`
- `wwwroot/img/publications/README-COVER-A.txt`
- `wwwroot/img/publications/covers/*` — five shipped artwork assets
- `tools/Test-PrismPublicationsPhase12.ps1`

There is **no EF migration** and **no database schema change**.

## Installation

The incremental Phase 12 package assumes Phase 11 is installed. Copy its contents over the
`ProjectManagement` project root, preserving directories.

Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase12.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

## Acceptance run

Regenerate the same brochure used for the Phase 11 comparison and verify:

- Cover A defaults to **Reference Original** and user can switch among all five artwork cards;
- no duplicated logos/title are painted over supplied Cover A artwork;
- CONTACTS does not overlap either agency heading;
- institutional Centre statement is integrated into the hero;
- normal project body is 9 pt;
- normal project images are visibly stronger and remain 16:9;
- no stretched justified continuation begins in the middle of a sentence;
- under-filled 3-project sheets use spare space intelligently;
- project order remains unchanged;
- final page ends on New Simulators without the repeated strapline;
- Gallery 2 and long two-line title cases still fit without clipping.

The original brochure remains the minimum visual benchmark. PRISM should retain its advantages in
consistency, deterministic pagination and clean standardized photography.
