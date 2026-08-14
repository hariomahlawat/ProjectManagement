# PRISM Publications Phase 28 — Proof-first Review Workspace

Phase 28 is a focused convergence release on top of Phase 27. It does not change the Compendium persistence schema and requires no EF migration.

## What changes

### Review becomes proof-first
- Adds a **Focus review** mode that temporarily reduces the Publication Structure rail and gives the live dossier proof substantially more width on large monitors.
- Adds **Fit / 75% / 100%** live-page zoom controls.
- Keeps the PDF Preview as the authoritative final renderer while making the HTML proof useful for genuine page inspection.
- Consolidates inspector scrolling: narrative content no longer introduces an additional desktop scrollbar.
- Compresses the duplicate source-image/facts inspector so the page proof is the dominant visual object.

### Publication Structure scales better
- Adds per-section collapse/expand controls without changing persisted section state or publication order.
- Adds **Collapse all / Expand all** controls.
- Collapsing is UI-only and never mutates saved Compendium structure.

### Final issue commands stay reachable
- Adds a compact viewport output dock when the normal Final Output card is outside the visible viewport.
- The dock mirrors Preview/Download eligibility and the current publication state.
- The dock disappears automatically when the canonical Final Output card is visible, avoiding duplicate commands on screen.

### Readiness findings become actionable queues
- Aggregated finding groups can launch **Review affected projects**.
- `Next requiring attention` becomes `Next affected` while that queue is active.
- Review & Next advances through the affected projects without cycling already-reviewed entries.

### Build identity
`CompendiumReadService.BuildStamp` is advanced to:

`CompendiumPdf_2026-08-14_publication-review-v7`

## Files changed / added

1. `Pages/Projects/Publications/Compendium/Index.cshtml`
2. `Services/Compendiums/CompendiumReadService.cs`
3. `wwwroot/css/pages/projects-publications.css`
4. `wwwroot/js/pages/projects-compendium.js`
5. `wwwroot/js/projects/publications-compendium-contract.test.js`
6. `ProjectManagement.Tests/Publications/CompendiumPhase28ContractTests.cs` *(new)*
7. `tools/Test-PrismPublicationsPhase28.ps1` *(new)*

## Installation

Copy the files from the ReadyToPaste package over the matching project-relative paths. No database migration is required.

Then run from the project root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase28.ps1
```

## Validation performed before packaging

- `node --check wwwroot/js/pages/projects-compendium.js` — PASS
- Compendium publication contract suite — **64 / 64 PASS**
- Combined Brochure + Compendium JS suite — **168 / 169 PASS**
  - The one failure is the pre-existing Brochure Phase 9 expectation that `IBrochurePrintMeasurementService` be registered as Singleton; the Phase 27 baseline already uses Scoped registration. Phase 28 does not modify Brochure DI.
- `git diff --check` against the Phase 27 source — PASS
- Ready-to-paste reconstruction is verified against the final Phase 28 source during packaging.
- .NET SDK is unavailable in the packaging environment, so `dotnet build` / `dotnet test` must be run on the development workstation using the included validation script.

## Recommended visual verification

1. Open Review on a 1920×1080 or wider monitor.
2. Toggle **Focus review** and confirm the proof gains width while Publication Structure remains usable.
3. Test **Fit / 75% / 100%** zoom.
4. Collapse and expand publication sections; confirm project order and saved section assignments do not change.
5. Scroll until the canonical Final Output card leaves the viewport; confirm the compact output dock appears and mirrors Preview/Download state.
6. Expand a grouped readiness warning and click **Review affected projects**; confirm review advances only through that affected set.
