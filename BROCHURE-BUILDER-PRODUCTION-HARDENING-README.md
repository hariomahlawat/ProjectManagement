# Brochure Builder — production hardening replacement set

Complete replacement files are supplied under their solution-relative paths. Copy the bundle
contents over the root of the `ProjectManagement` solution. No database migration or NuGet/npm
package change is required.

## Implemented behavior

### Print / Compact pagination correctness

- A project module is atomic in QuestPDF: `.ShowEntire()` wraps the complete fixed-height card, so
  a measurement mismatch cannot create an untitled continuation on the following sheet.
- SkiaSharp measurement reserves one normal 9 pt body line for QuestPDF shaping differences.
- Narrative splitting is guarded by an exact normalized reconstruction check; composition fails
  instead of duplicating or omitting source copy.
- Four modules remains the preferred reference density. A measured five-module sheet is allowed
  only when all 9 pt candidates really fit, with minimum-remaining-height pruning in the bounded
  search.
- User project order remains authoritative. Smart Flow remains an explicit, reversible suggestion.

### Whitespace policy

- The planner optimizes non-final sheets from the front and carries unavoidable residual space to
  the final sheet.
- The final sheet is excluded from underfill comparison, low-fill warning styling, average
  non-final fill, and cosmetic residual-padding expansion.
- Preflight identifies the final sheet as `final / residual allowed` and reports its fill
  separately.

### Cover A identity

- Every shipped Cover A artwork is classified as identity-complete.
- Neither the Print / Compact composer nor the Digital / Comfortable composer overlays additional
  organisation logos when that artwork is present.
- Separate logo assets remain available only for a missing-artwork fallback and Cover B.

### Review integrity

- Project approval is bound to a server-calculated SHA-256 fingerprint of the current project
  identity, narrative source and exact copy, image IDs/versions, crop coordinates, and treatment.
- Cover B approval is bound to the visible cover copy, publication profile, resolved hero image
  version, and crop.
- Any changed input clears the local approval. Final download also recalculates and verifies every
  fingerprint server-side, returning a blocker for a missing or stale review.
- Preview remains available after technical preflight; only final issue requires review approval.

### UI refinements

- Final-download readiness requires both the approval flag and its matching fingerprint.
- Preflight status distinguishes pending project review, pending Cover B approval, warnings, and
  fully complete issue readiness.
- The preflight card has one scroll owner rather than nested scrollbars.
- Truncated project names retain the full name as a tooltip.

## Files

Production:

- `Services/Publications/BrochureContracts.cs`
- `Services/Publications/BrochureInstitutionalCoverArtworkCatalog.cs`
- `Services/Publications/BrochurePrintLayoutMetrics.cs`
- `Services/Publications/BrochurePrintMeasurementService.cs`
- `Services/Publications/BrochurePrintPagePlanner.cs`
- `Services/Publications/BrochurePublicationService.cs`
- `Services/Publications/BrochureReviewFingerprint.cs` (new)
- `Utilities/Reporting/BrochurePdfReportBuilder.cs`
- `Utilities/Reporting/BrochurePrintCompactComposer.cs`
- `Pages/Projects/Publications/Brochure/Index.cshtml`
- `Pages/Projects/Publications/Brochure/Index.cshtml.cs`
- `wwwroot/css/pages/projects-publications.css`
- `wwwroot/js/pages/projects-brochure.js`

Regression coverage:

- `ProjectManagement.Tests/Publications/BrochurePrintCompactPlannerTests.cs`
- `ProjectManagement.Tests/Publications/BrochurePrintMeasurementServiceTests.cs`
- `ProjectManagement.Tests/Publications/BrochureReviewFingerprintTests.cs` (new)
- `wwwroot/js/projects/publications-brochure-contract.test.js`

## Validation

Run from the solution root:

```powershell
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~Publications"
node --check .\wwwroot\js\pages\projects-brochure.js
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js
```

Then regenerate the same nine-project Print / Compact benchmark and verify:

1. Every selected project occurs once, with title, complete narrative, and closing border.
2. The aerial-delivery narrative does not reappear as an untitled continuation.
3. Non-final sheets are packed first; any unavoidable residual is on the final sheet.
4. Cover A contains only the identity already embedded in the selected artwork.
5. Editing reviewed copy, imagery, treatment, crop, or Cover B content requires re-approval.

## Validation performed for this handoff

- `node --check wwwroot/js/pages/projects-brochure.js` — passed.
- Targeted brochure contract suite — 46/46 passed.
- The supplied four-page generated PDF was rendered and visually inspected: all nine project
  modules are atomic, the aerial-delivery copy has no orphan continuation, Cover A has no added
  organisation logo, and the deliberate residual appears on the final sheet.
- The aerial-delivery text does repeat inside its titled module because that repeated passage is
  already present in the selected Project Brief. Pagination must not silently edit authoritative
  source data; remove that source duplication in the project record if it is unintended.

The current execution environment does not include the .NET 8 SDK, so the .NET build and xUnit
suite must be run in the development/CI environment using the commands above before merge.
