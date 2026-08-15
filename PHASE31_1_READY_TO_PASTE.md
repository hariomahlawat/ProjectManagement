# PRISM Publications — Phase 31.1
## Adaptive Pagination & Authoring Fidelity Hardening

**Baseline:** Phase 31 — Adaptive Project Dossier Composer  
**Build identity:** `CompendiumPdf_2026-08-14_adaptive-pagination-v11`  
**Review contract:** `compendium-review-v6-adaptive-pagination`  
**Database migration:** **Not required**

Phase 31.1 is a hardening phase. It does not introduce a second publication model or change authoritative project facts. It corrects the two most important defects visible in the Phase 31 output: premature project continuation pages and the compressed wide-screen project register, while tightening browser/PDF parity.

## What changed

### 1. Geometry-aware project pagination

The legacy fixed first-page character budgets have been removed from `CompendiumPagePlanner`.

A new shared `CompendiumDossierPaginationPlanner` estimates the usable A4 dossier envelope from:

- project-title pressure;
- actual dossier family;
- photograph count;
- primary photograph height;
- narrative line-height/width pressure;
- programme-information module count;
- technical-specification height;
- longest technical-specification item.

For **Automatic** layout, the planner now tries to keep a project on one page by:

1. retaining the recommended family when it fits;
2. progressively yielding photograph height;
3. trying a more space-efficient controlled dossier family when appropriate;
4. creating a continuation page only when the safe one-page envelope is genuinely exceeded.

The normal target is that an ordinary one-photo dossier with a roughly 150–220 word Project Brief remains on one page when its other content permits it. Explicit publisher layout overrides are respected; extreme content can still produce controlled continuation pages.

### 2. Orphan-continuation suppression

A small narrative tail is no longer automatically pushed to an almost-empty second page simply because it crosses a conservative character threshold. The shared planner applies a bounded geometric tolerance after photography has already yielded space.

When continuation is unavoidable, technical specifications are moved before creating unnecessary narrative fragmentation, and a modest technical continuation can share the final narrative continuation where safe.

### 3. Live Review explains page-fit decisions

Review now receives and displays:

- resolved dossier family;
- pressure score;
- planned primary-image height;
- estimated dossier page count;
- pagination note;
- pagination reason.

The browser proof also shows a clear continuation cue when the dossier is expected to use more than one page. The live proof remains a fast near-final composition; the generated PDF remains authoritative.

### 4. Browser/PDF image-geometry parity

Effective DPI is now calculated against the **actual planned dossier frame**, including the resolved family and planned image height, rather than a generic project-image frame.

The same dynamic image height is passed into the PDF renderer and the browser proof.

### 5. Technical-specification layout hardening

Column choice is no longer based only on aggregate character count.

The renderer now considers both total pressure and the **longest individual bullet**:

- short 4–6 item sets may use three columns;
- moderate sets may use two columns;
- a single long technical requirement forces a wider/single-column treatment even when the overall total is modest.

### 6. Programme-information grid hardening

Programme Information remains completely modular. Missing facts still leave no empty boxes.

Four available modules now use a readable **2 × 2** arrangement rather than four narrow quarter-width cells. One, two and three modules retain one-, two- and three-column arrangements respectively.

### 7. IPR publication credentials no longer truncate

The PDF and browser proof no longer take only the first two IPR records.

All Filed/Granted publication credentials are aggregated by IPR type into one compact IPR module. Multiple records and relevant years are summarised without creating blank or excessively narrow programme cells.

### 8. Continuation heading corrected

The duplicated heading pattern:

`PROJECT BRIEF · CONTINUED            CONTINUED · 2`

has been replaced by a cleaner continuation grammar:

`PROJECT BRIEF                         CONTINUED · PART 2`

Technical-only continuations use `TECHNICAL REFERENCE`.

### 9. Select Projects wide-screen regression corrected

The candidate register no longer collapses to `min-width: 0` on wide screens while retaining fixed nowrap cells.

Phase 31.1 restores a protected register width, gives Project the highest information priority, allows appropriate metadata wrapping, and line-clamps long project names to two lines. Horizontal overflow remains local to the register when the available publication workspace is genuinely narrower than the information surface.

### 10. Review integrity advanced

The review-fingerprint contract is now:

`compendium-review-v6-adaptive-pagination`

This is deliberate. Project approvals created under the old pagination contract are invalidated so that a publisher reviews the page under the new layout/pagination behaviour before final issue.

## PDF navigation

The existing Compendium Index and internal section/project links are retained. Phase 31.1 does **not** add a new PDF post-processing package solely to manufacture sidebar outline/bookmark nodes. That enhancement is intentionally deferred to an isolated compatibility phase rather than introducing a new PDF-rewrite dependency into the air-gapped production stack during pagination hardening.

## Changed / new files

- `Services/Compendiums/CompendiumDossierPaginationPlanner.cs` **(new)**
- `Services/Compendiums/CompendiumDtos.cs`
- `Services/Compendiums/CompendiumReadService.cs`
- `Services/Compendiums/CompendiumExportService.cs`
- `Services/Compendiums/CompendiumReviewFingerprint.cs`
- `Utilities/Reporting/CompendiumPagePlanner.cs`
- `Utilities/Reporting/CompendiumPdfReportBuilder.cs`
- `Pages/Projects/Publications/Compendium/Index.cshtml`
- `Pages/Projects/Publications/Compendium/Index.cshtml.cs`
- `wwwroot/js/pages/projects-compendium.js`
- `wwwroot/css/pages/projects-publications.css`
- `wwwroot/js/projects/publications-compendium-contract.test.js`
- `wwwroot/js/projects/publications-compendium-phase30-1-contract.test.js`
- `wwwroot/js/projects/publications-compendium-phase31-contract.test.js`
- `wwwroot/js/projects/publications-compendium-phase31-1-contract.test.js` **(new)**
- `ProjectManagement.Tests/Publications/CompendiumPhase31_1PaginationTests.cs` **(new)**
- `tools/Test-PrismPublicationsPhase31_1.ps1` **(new)**

## Installation

Use the Ready-to-Paste ZIP over the existing **Phase 31** source tree.

From the project root, for example:

```powershell
cd "E:\Dot Net Web Development\ProjectManagement"
```

Extract `PRISM_Publications_Phase31_1_ReadyToPaste.zip` into that directory and allow overwrite of the listed files.

No EF migration is required.

Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase31_1.ps1
```

The script performs JavaScript syntax/contract checks and, when the .NET SDK is available, runs:

```powershell
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~Compendium"
```

After a successful build, generate a fresh Compendium PDF and specifically verify:

- representative 150–220 word briefs no longer produce tiny orphan continuation pages;
- genuinely heavy dossiers still create controlled continuations;
- Automatic can reduce image height before pagination;
- project page count shown in Review matches the generated dossier;
- long technical bullets remain readable;
- the Select Projects register does not collide at 1920 px / wide-monitor layouts.

## Validation performed in this delivery environment

- `node --check wwwroot/js/pages/projects-compendium.js` — **PASS**
- Compendium JavaScript contract suite — **112 / 112 PASS**
- changed C# delimiter/string structural sanity checks — **PASS**
- legacy `ResolveDossierNarrativeBudget` absent from production planner — **PASS**
- IPR two-record truncation absent from production renderer/browser code — **PASS**
- relative patch whitespace check — **PASS**

The .NET SDK is not installed in this execution environment, therefore this package does **not** claim a `dotnet build`, `dotnet test`, or regenerated Phase 31.1 PDF pass here. The supplied validation script performs the .NET checks on the development workstation.
