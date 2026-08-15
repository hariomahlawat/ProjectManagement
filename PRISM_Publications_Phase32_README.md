# PRISM Publications — Phase 32
## Adaptive Composition & Editorial Polish

Phase 32 is a no-schema publication hardening release built on Phase 31.1. It keeps the existing Compendium workflow and controlled dossier layout families, but upgrades the page planner from a simple first-fit mechanism to a ranked composition engine.

## What changes

### 1. Composition-quality scoring
`CompendiumDossierPaginationPlanner` now evaluates every valid one-page candidate and ranks it using:
- residual whitespace balance;
- image utilisation;
- narrative readability;
- layout suitability for content pressure;
- technical-reference density;
- publisher-selected layout preference.

The planner can invest spare space in larger photography and modestly larger narrative typography while retaining the existing safe A4 one-page envelope and deterministic continuation fallback.

### 2. Shared adaptive narrative typography
The planner emits `DossierNarrativeFontScale` (1.00–1.08). The value is carried through review JSON and export DTOs into the QuestPDF builder and mirrored by the browser proof.

### 3. More conservative technical specification layout
Three-column technical specifications are now reserved for short fragments. Medium/long requirements use two or one columns. The final PDF technical bullet type has also been increased for print readability.

### 4. Truthful page-photography UX
The Review workspace now distinguishes:
- curated page imagery;
- imagery actually displayed by the current layout;
- supporting images retained for Automatic/Multi-image use.

Visual Hero, Balanced and Technical remain intentionally single-image layouts without destroying previously curated supporting images. Selecting Multi-image automatically promotes the curated slot count to two when usable photography is available.

### 5. Programme Information polish
Programme labels use adaptive typography when two/three columns are required. The IPR/ToT badges are optically stronger without becoming dominant.

### 6. Browser/PDF editorial order parity
Multi-image dossier pages now render imagery before narrative in the final PDF, matching the live proof.

### 7. Cover proof viewport controls
The Cover Editor gains:
- Fit (default)
- 75%
- 100%

Fit recalculates from the available proof viewport. Template and Front/Back changes reset to Fit and the proof scroll position is reset, so the complete A4 composition can always be assessed before zooming in.

## No database migration
Phase 32 does **not** alter the database schema or preset schema. Existing Phase 31/31.1 data remains valid.

The review fingerprint contract advances to `compendium-review-v7-adaptive-composition`, so previously reviewed projects can correctly require re-review after composition logic changes.

## Build identity
`CompendiumPdf_2026-08-15_adaptive-composition-v12`

## Ready-to-paste files
Copy the contents of the ReadyToPaste package into the project root, preserving folders and replacing files when prompted.

## Validation
Run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase32.ps1
```

The script performs JavaScript syntax checks, publication contract tests, build-contract checks and, when the .NET SDK is available, `dotnet build` plus Compendium-focused tests.

Recommended final checks on the development workstation:

```powershell
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~Compendium"
node --check .\wwwroot\js\pages\projects-compendium.js
node --check .\wwwroot\js\pages\projects-compendium-cover-editor.js
node --test .\wwwroot\js\projects\publications-compendium*.test.js
```

## Functional acceptance checks
1. Open a sparse one-image project with Automatic/Visual Hero and confirm the proof uses the available page more confidently without overflow.
2. Open a project with four-to-six hardware requirements including one long requirement; confirm it falls back to two or one columns.
3. Curate three images, switch to Balanced/Technical, and confirm the UI states that one image is displayed while supporting images are retained.
4. Switch back to Multi-image and confirm supporting photographs immediately become available.
5. Verify Automatic and explicit layouts remain stable through Save/Load.
6. Open Cover Editor and verify Fit/75%/100%, Front/Back reset and template reset behaviour.
7. Preview/download the PDF and compare the live proof with final PDF, especially Multi-image image-before-narrative order.
8. Exercise IPR cases: Patent Filed, Patent Granted, Copyright Granted, and mixed Patent + Copyright.

