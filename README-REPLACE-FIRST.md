# PRISM Publications — Phase 14
## Adaptive Editorial Pagination & Smart Flow

This package replaces the Phase 13 Print / Compact pagination engine with an adaptive 9 pt composition optimiser while preserving the approved publication design.

## Objective

The hard-copy brochure must remain **equal to or better than the original reference brochure**. Phase 14 improves sheet utilisation without reducing normal body typography or exposing low-level design controls to the user.

## What Phase 14 changes

### 1. Adaptive 9 pt project layouts
Normal print copy remains 9 pt. Every project is measured into a bounded set of Visual, Balanced and **Dense 9 pt** candidates. Dense candidates reduce line rhythm, paragraph rhythm, padding and image width — not font size.

Approved normal image range: **132–156 pt**, exact **16:9**.

8.5 pt Compact remains available only when an individual project cannot fit on a complete sheet at any valid 9 pt geometry.

### 2. Pareto candidate frontier
The measurement service generates multiple valid layouts and discards dominated alternatives. The page planner receives at most six useful height/visual-quality candidates per project, keeping optimisation deterministic and bounded.

### 3. Automatic image mode is genuinely automatic
- **Single** — exactly one image.
- **Gallery 2** — exactly two selected images; the planner may not remove the second image.
- **Automatic** — if a second image is selected, the planner may use either one or two images according to page geometry.

The renderer consumes the exact image treatment chosen by the planner.

### 4. Smart Flow adviser
PDF generation continues to preserve the user's current publication order exactly.

Preflight separately evaluates a bounded local-order alternative. If it materially improves the publication, PRISM shows:
- current vs suggested sheet count;
- lowest-sheet fill improvement;
- exact projects proposed for movement;
- adaptive treatment summary;
- suggested sheet map;
- **Apply suggested order** and **Undo order change**.

PRISM never reorders projects silently.

Smart Flow is intentionally conservative: maximum local movement is three positions, the search is beam-bounded, page-count reduction dominates the objective, and editorial displacement is penalised.

### 5. Residual-space handling is final polish only
Once page membership is selected, remaining space may be used for modest card breathing room and inter-project spacing. It does not repaginate projects and does not enlarge photographs after planning.

### 6. Cover A identity hardening
`Reference Original` remains complete artwork and receives no institutional overlay.

The four generated alternatives are now **background-only** 1600×1280 hero assets. Generated shield/archer identity has been removed from the publication-safe crop. PRISM overlays the exact deployed institutional identity assets at render time.

### 7. Actionable preflight
When Smart Flow is available, preflight tells the editor what can improve, rather than merely reporting an under-filled sheet. If no material order change helps, under-utilisation is explicitly reported as structural after the adaptive 9 pt engine has tested its valid geometry.

## Files to replace

Copy the package contents over the same paths in the PRISM project. The Phase 14 incremental ZIP contains only files changed from Phase 13 plus all five Cover A assets.

Key source files:
- `Services/Publications/BrochureContracts.cs`
- `Services/Publications/BrochureInstitutionalCoverArtworkCatalog.cs`
- `Services/Publications/BrochurePrintLayoutMetrics.cs`
- `Services/Publications/BrochurePrintMeasurementService.cs`
- `Services/Publications/BrochurePrintPagePlanner.cs`
- `Services/Publications/BrochurePublicationService.cs`
- `Utilities/Reporting/BrochurePdfReportBuilder.cs`
- `Utilities/Reporting/BrochurePrintCompactComposer.cs`
- `Pages/Projects/Publications/Brochure/Index.cshtml`
- `Pages/Projects/Publications/Brochure/Index.cshtml.cs`
- `wwwroot/js/pages/projects-brochure.js`
- `wwwroot/css/pages/projects-publications.css`

Regression coverage:
- `ProjectManagement.Tests/Publications/BrochurePrintMeasurementServiceTests.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintCompactPlannerTests.cs`
- `wwwroot/js/projects/publications-brochure-contract.test.js`
- `tools/Test-PrismPublicationsPhase14.ps1`

## Installation

From the project root after replacing the files:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase14.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

## Acceptance run

Regenerate the same nine-project test brochure used for Phase 13.

Verify:
1. Normal project body remains 9 pt.
2. No project image is distorted; normal print width stays inside 132–156 pt.
3. Current publication order remains unchanged until Smart Flow is explicitly applied.
4. If Smart Flow is offered, review the exact move list and suggested sheet map before applying it.
5. Automatic may use one selected image when doing so improves composition; explicit Gallery 2 must remain two images.
6. No project/title clipping or overlap occurs.
7. Planner sheet mapping matches actual PDF page membership.
8. Compare final sheet density and visual balance directly with the original brochure.

The acceptance standard remains **equal to or better than the original brochure**, not simply fewer pages.
