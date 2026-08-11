# PRISM Publications — Phase 11 Print Fidelity & Composition Quality Lock

## Purpose

Phase 11 is the final hard-copy quality pass on top of the Phase 10 reference-fidelity renderer.
It is intentionally isolated to **Print / Compact**. Digital / Comfortable, brochure selection,
review, photo editing, approval and preflight workflows are unchanged.

The phase addresses the remaining defects visible in the latest generated brochure:

- Cover A `CONTACTS` overlay colliding with the Manufacturing Agency heading;
- 16:9 publication photographs being measured inside taller non-16:9 print frames;
- Balanced pages dropping to 8.75 pt merely to protect page count;
- float text occasionally continuing at full width from the middle of a sentence;
- under-filled sheets not using safe residual space to strengthen imagery.

## What changed

### 1. Exact 16:9 project-image geometry

The publication photo pipeline already normalises selected project images to a 16:9 publication
crop. Print / Compact now measures and renders the same geometry:

- Single image frame: **16:9**;
- Gallery 2 image frames: **16:9** each;
- Visual image width: approximately **150 pt** before bounded narrative adjustment;
- Balanced image width: approximately **140 pt**;
- Compact image width: approximately **130 pt**.

The extra one-point white inset inside the final image frame has also been removed. This eliminates
the artificial white strip caused by placing a 16:9 image inside the old 1.45 / 1.65 aspect boxes.

### 2. Nine-point typography is now a planning constraint

Visual and Balanced layouts both retain the normal **9 pt** project body. Compact remains an
emergency layout at the existing **8.5 pt hard floor**.

The page planner now compares solutions in this order:

1. lowest typography penalty;
2. lowest page count;
3. best measured image quality / utilisation score.

Therefore an additional sheet is preferred over reducing normal project copy below 9 pt.
The planner also compares a shared-closing solution against a dedicated closing sheet, so the
closing block cannot force an otherwise avoidable typography reduction.

### 3. Editorially-aware float splitting

The measured right-image text wrap is retained, but the split no longer blindly ends at the last
word that fits beside the image.

The measurement service now searches nearby boundaries in this order of preference:

- paragraph boundary;
- sentence boundary;
- complete-word fallback.

A bounded line-height tolerance keeps the semantic boundary close to the actual image height.
This prevents the full-width justified continuation from beginning awkwardly in the middle of a
sentence in normal cases.

### 4. Bounded residual-space image expansion

After project membership for each sheet is locked, Phase 11 performs a second measured pass on
under-filled pages. It enlarges eligible project images in 4 pt width increments, up to a strict
24 pt boost / 176 pt maximum width, while:

- preserving project order;
- preserving page membership;
- preserving the selected typography variant;
- never exceeding physical sheet capacity;
- stopping near the 95% target fill when further expansion would worsen the result.

This improves short three-project sheets without stretching text, creating artificial blank card
height or forcing another project onto the page.

### 5. Cover A contact footer is structurally safe

`CONTACTS` is no longer an overlay painted on top of two equal agency columns. The footer now has
an explicit heading row:

`Developing Agency | CONTACTS | Manufacturing Agency`

and a separate two-column content row below it. The measurement service reserves the additional
header height, so the first-page footer remains stable at all supported contact font sizes.

### 6. Institutional artwork contract is explicit

`wwwroot/img/publications/README-COVER-A.txt` now defines `cover-a-institutional.*` as
**background artwork only**. Logos, title, edition, handling marking, Centre statement, contacts
and strapline must not be baked into the image; PRISM overlays those live values.

## Files changed by Phase 11

- `Services/Publications/BrochurePrintLayoutMetrics.cs`
- `Services/Publications/BrochurePrintMeasurementService.cs`
- `Services/Publications/BrochurePrintPagePlanner.cs`
- `Utilities/Reporting/BrochurePrintCompactComposer.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintMeasurementServiceTests.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintCompactPlannerTests.cs`
- `wwwroot/js/projects/publications-brochure-contract.test.js`
- `wwwroot/img/publications/README-COVER-A.txt`
- `tools/Test-PrismPublicationsPhase11.ps1` (new)

There is **no EF migration**, **no database change**, **no Program.cs change**, **no Razor/UI
change**, and **no CSS/client workflow change**.

## Installation

This incremental package assumes **Phase 10 is already installed**.

Copy the package contents over the `ProjectManagement` project root and preserve the directory
structure. Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase11.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\projects-brochure.js
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js
```

## Acceptance checks

Generate the same representative Print / Compact brochure used for Phase 10 and verify:

1. `CONTACTS` no longer obscures either agency heading on Cover A.
2. Single project images fill their frames without the former white band below the photograph.
3. Gallery 2 displays two clean 16:9 images stacked on the upper-right.
4. Normal Visual and Balanced project modules render at 9 pt body typography.
5. 8.5 pt is used only when an individual/combined layout genuinely cannot be planned at 9 pt.
6. A dedicated closing sheet is preferred when sharing the closing matter would otherwise require
   avoidable typography reduction.
7. Full-width continuation normally begins at a paragraph or sentence boundary, not in the middle
   of a sentence.
8. Under-filled project sheets use bounded larger imagery where measured space permits.
9. Project order and the preflight sheet map remain unchanged and deterministic.
10. Long two-line project titles grow the green title band rather than collapsing typography.
11. Gallery 2 is tested with short, medium and long narratives.
12. Digital / Comfortable output remains unchanged.

## Validation completed in this preparation environment

- `node --check wwwroot/js/projects/publications-brochure-contract.test.js` — passed.
- `node --test wwwroot/js/projects/publications-brochure-contract.test.js` — **36/36 passed**.
- Structural C# delimiter/string-state scan — passed for all Phase 11 modified C# files.

The preparation environment does not expose the .NET SDK. Run `dotnet restore`, `dotnet build`
and the xUnit suite on the PRISM development machine before deployment.
