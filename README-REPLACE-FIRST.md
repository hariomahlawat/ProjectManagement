# PRISM Publications — Phase 7 Compact Print Profile

## Purpose

Phase 7 adds a purpose-built **Print / Compact** brochure profile modelled on the effective page geometry and information architecture of the supplied approved hard-copy brochure, while preserving the existing **Digital / Comfortable** A4 profile.

The supplied Canva PDF was rechecked at PDF page-box level. Its effective page size is **423.23 × 846.755 points** (approximately **149.3 × 298.7 mm**), not A4. The new hard-copy compositor uses those exact effective dimensions.

The reference brochure also uses content-bearing first and final pages. Phase 7 therefore retains editable publication-level institutional text in addition to project briefs:

- opening simulator/technology narrative;
- Centre of Expertise statement;
- procurement guidance;
- Developing Agency / contact details;
- Manufacturing Agency details;
- Visionary Horizons & Strategic Objectives;
- New Simulators guidance;
- institutional strapline.

These values are publication-level defaults. They do **not** write back to Project records.

## Installation — recommended for the current Phase 6 installation

Copy the contents of the Phase 7 incremental ZIP over the **ProjectManagement project root**, preserving paths and replacing matching files.

There is:

- **no EF migration**;
- **no Program.cs change**;
- **no navigation change**;
- **no Compendium change**;
- **no font reinstall**.

## Phase 7 behaviour

### Print / Compact — default

- Exact effective reference size: **423.23 × 846.755 pt** (~149.3 × 298.7 mm).
- Purpose-built slim hard-copy renderer; it does not use A4.
- Content-bearing first page rather than a sparse decorative cover.
- Project modules are measured and flowed vertically by QuestPDF; they are not forced into the Digital `SingleFeature` / `TwoFeature` geometry.
- Compact print typography and tactical one/two-image treatment.
- Each project module stays intact; the next project begins immediately after it whenever space permits.
- Final institutional matter follows the last project, allowing the final physical page to contain both projects and the Visionary/New Simulators sections when space permits.
- Long project narratives above the compact-print safety threshold are blocked by preflight rather than silently shrinking typography to unreadable sizes.
- Cover B remains independently selectable/croppable and uses a profile-specific source crop.

### Digital / Comfortable

The existing Phase 6 A4 renderer remains isolated and unchanged in principle:

- A4 layout;
- current Cover A/B choices;
- digital introduction/back-cover controls;
- spacious 1–4 project layout planning;
- Phase 6 SingleFeature / TwoFeature composition.

### Image-control fixes

Phase 7 also fixes the Review interaction identified during Phase 6 acceptance:

- **Change image** opens the photograph selector.
- **Adjust crop** opens and focuses the focal-point crop editor for the current primary image.
- Cover **Change hero** brings the chooser into view.
- Cover **Adjust crop** brings and focuses the cover crop editor.

## Verify after copying

From the ProjectManagement root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase7.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\projects-brochure.js
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js
```

## Browser acceptance checks

1. Open Publications → Brochure. **Print / Compact** should be selected by default.
2. Switch between Print / Compact and Digital / Comfortable. Print institutional fields and Digital-only introduction/back-cover controls should switch cleanly.
3. In Print / Compact, open **Hard-copy institutional content** and verify the front/final publication text is populated and editable.
4. Select 8–12 projects and complete technical preflight.
5. In Review, verify **Change image** opens photograph selection while **Adjust crop** goes directly to the focal editor.
6. Verify Cover B hero selection/crop still works independently of project image/order.
7. Preview Print / Compact and check the PDF page proportions are the same slim portrait proportions as the supplied hard-copy brochure—not A4.
8. Verify the first print page contains the institutional narrative, procurement and agency/contact content.
9. Verify project pages are densely flowed with no Digital-style giant feature-page whitespace.
10. Verify the final print page (or, if necessary because of content volume, the next complete page) contains **Visionary Horizons & Strategic Objectives** and **New Simulators** after the final project modules.
11. Switch to Digital / Comfortable and confirm the existing A4 output remains available.

## Preparation-environment validation

Completed here:

- Rechecked the supplied reference PDF with `pdfinfo -box`: effective page size **423.23 × 846.755 pt**, CropBox/TrimBox matching that effective page.
- `node --check wwwroot/js/pages/projects-brochure.js` — passed.
- `node --test wwwroot/js/projects/publications-brochure-contract.test.js` — **19/19 passed**.
- Modified C# files passed structural delimiter checks.
- Publications CSS passed structural brace validation.
- Phase 7 source contracts were cross-checked against the generated checker patterns.
- All `BrochureBuildOptions` and brochure preflight call sites in the supplied Publications package were checked for the new profile parameter.

The preparation environment does not contain the .NET SDK or PowerShell, so `dotnet build`, xUnit execution and the supplied PowerShell integration checker must be run in the normal PRISM development environment.
