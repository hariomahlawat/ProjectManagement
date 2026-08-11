# PRISM Publications — Phase 5 Ready-to-Replace

## Phase 5: Editorial Review & Brochure Renderer Maturity

This package is designed to be copied over a working **Phase 4** Publications installation. It is migration-free and does not replace `Program.cs`, Project navigation, Compendium services, or publication fonts.

The changes are based on the first actual PRISM-generated capability brochure and focus on the remaining publication-quality gaps rather than adding unrelated features.

## Implemented

### Cover B
- Cover B closing band is anchored to the A4 bottom edge; the previous unexplained lower white tail is removed.
- The front cover no longer displays `Generated from authoritative PRISM records` or a visible project-count provenance line.
- Cover B has a dedicated hero-selection workflow independent of project ordering.
- Automatic hero selection remains available and prefers the strongest usable image according to the existing publication quality classification.
- An explicitly chosen hero survives project reorder and uses the project's brochure focal point.
- Cover-hero quality is rechecked by authoritative preflight.

### Publication Review
- Added **Step 3 — Review publication**.
- Review shows the currently selected authoritative narrative and the actual selected brochure image together.
- The user can confirm the automatic image, change/crop imagery, open the authoritative project content, manage photographs, and mark the project reviewed.
- Review is session/request state only; no duplicate project narrative or database record is created.
- Preview requires technical preflight to pass.
- Final Download additionally requires every selected project to be reviewed.
- Review state is invalidated when material publication inputs change, including narrative source, project order, imagery or focal point.

### Cross-tab refresh
- Added an authenticated `ProjectState` handler.
- Returning from Project Brief/Capability/Description or Photo management refreshes selected project narrative/photo state without losing brochure order or configuration.
- If source content or photo version changed, the affected project review is invalidated.
- The source link follows the selected narrative source (`brief`, `capabilities`, or `description`).

### Two-project feature pages
- Added a dedicated `TwoFeature` renderer rather than stretching the generic card renderer.
- Project imagery increases to a 205 pt editorial frame.
- Image position alternates right/left between the two project modules.
- Full empty bordered rectangles are removed; unused room becomes deliberate page whitespace.
- TwoFeature is now limited to approximately 185-word fragments. Heavier copy promotes to SingleFeature instead of compressing the feature layout.
- ThreeStandard and FourCompact remain fundamentally unchanged.

### Final PDF handling
- Preview and Download now use `fetch` + PDF Blob rather than a normal file form POST.
- The UI reliably exits `Preparing brochure…` after the response.
- Server errors are surfaced and buttons restore correctly.
- The server supplies the final filename through `X-PRISM-Publication-FileName`.

### Back cover and metadata
- Added an optional institutional closing/back cover, enabled by default.
- PDF metadata identifies the document as an SDD capability publication created by PRISM Publications.

### UX refinement
- `Select visible` is replaced by accurate `Select N matching` / `Select first N matching` language.
- Desktop category-filter widths are increased to avoid clipped labels.
- The right rail no longer uses one full-height nested scrollbar; Publication Order, image editor and preflight have controlled bounded regions while export actions remain available.

## Deliberately not implemented

- No duplicate Project Brief detection. The duplicate copy observed in the test brochure was test data and is not treated as a production requirement.
- No AI image-content relevance judgement.
- No saved brochure/database persistence.
- No publication-history migration.
- No change to existing Compendium logic.
- No new permission model.
- No new fonts.

## Installation

1. Ensure Phase 4 is already working.
2. Copy the contents of this package over the **ProjectManagement project root**, preserving relative paths and replacing matching files.
3. No EF migration is required.
4. No `Program.cs` merge is required.
5. No navigation merge is required.
6. No font reinstall is required.

Run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase5.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\projects-brochure.js
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js
```

Restart IIS/IIS Express and hard-refresh after the successful build.

## Acceptance checks

1. Select 2 projects with valid Project Briefs and photographs.
2. Confirm Cover B shows an Automatic hero and allows `Change hero` and `Adjust crop`.
3. Explicitly choose a hero, reorder the projects, and confirm the chosen hero remains unchanged.
4. Confirm reordering reruns preflight and invalidates Publication Review.
5. Open Step 3 and confirm the authoritative current narrative and brochure photograph are shown together.
6. Mark one project reviewed. Confirm the publication order shows the reviewed state.
7. Change that project's brochure image or focal point and confirm the review is invalidated.
8. Open the authoritative project content in a new tab, edit/save it, return to Brochure, and confirm the new content/word count refreshes.
9. Confirm Preview becomes available once technical blockers are zero, even if review is incomplete.
10. Confirm Download stays disabled until all selected projects are reviewed.
11. Preview/download and confirm the action returns from `Preparing...` after the PDF response.
12. Confirm Cover B has no unexplained white tail below the closing band.
13. Confirm a 2-project page uses noticeably larger imagery with alternating image placement and no large empty bordered rectangles.
14. Confirm the optional back cover is present by default and disappears when disabled.
15. Confirm 3- and 4-project page types remain stable.
16. Confirm Compendium is unaffected.

## Recommended next step

After this phase passes build/runtime checks, generate a real 10–15 project brochure using production-quality Project Briefs and appropriate photographs. Review both Cover A and Cover B page-by-page against the Canva reference before introducing saved brochure persistence.
