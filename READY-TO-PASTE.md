# PRISM Publications Phase 27

## Publication polish, wide-monitor efficiency and Capability Dossier review

This package is intended to be applied on top of the Phase 26 Publications implementation.

### What this phase changes

- Keeps the Compendium as a true full-width authoring workspace and compresses non-authoring chrome so more of the publication is visible at once.
- Makes the right rail viewport-aware: Publication Structure is the scrolling region and Final Output remains available at the bottom of the rail on desktop.
- Rebuilds Project Selection as a wide publication register with separate Lifecycle, Project Category, Technical Category, Narrative, Arm/Service, Cost and Photography columns.
- Makes custom-section renaming visibly discoverable while retaining direct inline editing and drag/drop section/project ordering.
- Adds a near-WYSIWYG A4 Capability Dossier preview to Review. The HTML preview mirrors the selected section, status, facts, photograph/crop and effective narrative; PDF Preview remains authoritative.
- Removes repeated section/status information from PDF running headers. Project pages now use the publication title + edition in the running header and keep section/status once in the dossier masthead.
- Makes the PDF facts panel adaptive to the actual number of available facts instead of leaving empty three-column cells.
- Replaces raw narrative character thresholds with a deterministic rendered-line pressure estimator shared by image geometry and page planning.
- Improves Automatic Hero selection by prioritising intentional project-cover/marked-cover sources before arbitrary available imagery.
- Makes `Latest first` lifecycle-aware: completed projects use completion chronology; ongoing projects use project/development year, with database creation time only as a final fallback.
- Uses authoritative `TechnicalCategory.SortOrder` for Technical Category publication sections; ordering modes only reorder projects inside those sections.
- Adds Phase 27 contract coverage and a workstation validation script.

### Persistence / database

No new database migration is required for Phase 27. The Phase 26 first-class custom-section schema remains unchanged.

### Ready-to-paste files

The ReadyToPaste ZIP preserves project-relative paths. Extract it over the project root and replace the matching files.

Changed/new files:

1. `Pages/Projects/Publications/Compendium/Index.cshtml`
2. `Pages/Projects/Publications/Compendium/Index.cshtml.cs`
3. `Services/Compendiums/CompendiumDtos.cs`
4. `Services/Compendiums/CompendiumExportService.cs`
5. `Services/Compendiums/CompendiumReadService.cs`
6. `Utilities/Reporting/CompendiumPagePlanner.cs`
7. `Utilities/Reporting/CompendiumPdfReportBuilder.cs`
8. `wwwroot/css/pages/projects-publications.css`
9. `wwwroot/js/pages/projects-compendium.js`
10. `wwwroot/js/projects/publications-compendium-contract.test.js`
11. `ProjectManagement.Tests/Publications/CompendiumPhase27ContractTests.cs` (new)
12. `tools/Test-PrismPublicationsPhase27.ps1` (new)

### Validation performed in this environment

- `node --check wwwroot/js/pages/projects-compendium.js` — PASS
- Compendium JavaScript publication contracts — **59/59 PASS**
- Combined Brochure + Compendium publication contracts — **163/164 PASS**. The single Brochure failure is the pre-existing Phase 9 DI expectation for `IBrochurePrintMeasurementService`; the identical test fails in the unmodified Phase 26 baseline.
- Structural delimiter checks on all changed C# files — PASS
- ZIP integrity checks — PASS (performed after packaging)

The .NET SDK is not installed in this execution environment, therefore `dotnet build` and `dotnet test` could not be run here.

### Workstation validation

From the project root in PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase27.ps1
```

The script runs the JavaScript contracts first, preserves the Phase 26 persistence checks, verifies the new Phase 27 source contracts, and then runs `dotnet build` / `dotnet test` when the .NET SDK is available.

### Recommended functional smoke test

1. Load an existing Phase 26 Compendium.
2. Confirm the right rail remains visible while scrolling and Final Output stays accessible.
3. Switch between Technical Category / No grouping / Custom sections and confirm section ordering remains stable.
4. In Custom sections, rename a section and drag projects between sections.
5. Select `Latest first`; confirm custom section order does not change and completed projects are ordered by completion chronology.
6. Open Review and verify the live A4 dossier updates for narrative source, selected image and crop.
7. Preview the PDF and verify:
   - publication title + edition appear in the project-page running header;
   - section/status appear only once in the dossier masthead;
   - metadata grids do not contain blank filler cells;
   - custom section/index order is correct;
   - Automatic Hero prefers designated cover imagery.
8. Review all selected projects and generate the final Compendium PDF.
