# PRISM Project Publications + Capability Brochure

## What this package implements

This phase creates one **Publications** entry point under Projects and keeps the two publication purposes separate beneath it:

- **Capability Brochure** — new, visual A4 publication workflow.
- **Simulators Compendium** — existing detailed proliferation catalogue, using the existing Compendium read/export services and renderer.

The legacy `/Projects/Compendium` route remains compatible. GET requests move users to the canonical Publications workspace; a legacy `Generate` POST still invokes the existing Compendium exporter.

## Brochure behaviour implemented

- Project Brief is the default and recommended project narrative source.
- A global selector also permits Capability Overview or Full Description.
- Sources are never merged automatically.
- Cover A: **Institutional / Evolutionary**.
- Cover B: **Contemporary / Premium**.
- Project selection supports search, lifecycle, project-category and technical-category filters.
- Selected projects can be reordered by drag-and-drop or move-up/move-down buttons.
- Cover B uses the first selected project with a usable photograph as its hero image.
- Cover A uses up to the first three selected project photographs as an institutional montage.
- Brochure preflight updates for the selected narrative source and reports:
  - missing narrative;
  - missing photograph;
  - low-resolution selected photograph;
  - long copy over 210 words.
- Layout is deterministic and adaptive:
  - up to 4 concise projects per page;
  - 3 standard projects per page;
  - 2 longer projects per page;
  - exceptional long copy is split into continuation feature pages.
- Project body typography is not reduced below the publication floor simply to force another card onto a page.
- PDF generation uses live PRISM records and does not modify project data.
- No database migration is required in this phase.
- No new NuGet package and no `Program.cs` replacement are required.

## Existing Compendium

The Compendium's existing eligibility and generation services are retained. This package does **not** change `CompendiumReadService`, `CompendiumExportService` or `CompendiumPdfReportBuilder`.

The new Publications/Compendium page is a common-workspace shell over those existing services. Publication warnings remain accessible, with the first eight shown initially and a `Show all` action for larger warning sets.

## Offline publication fonts

The brochure code has an explicit publication-font registry. It never needs an internet font request.

Preferred primary family:

`DM Sans`

Optional Cover A display accent:

`Alatsi`

Font binaries are intentionally not part of this source package. Place organisation-approved/licensed static TTF files at the exact paths documented in:

`wwwroot/fonts/publications/README-PUBLICATION-FONTS.txt`

If DM Sans is absent or cannot be registered, the brochure safely falls back to QuestPDF's bundled Lato family. Restart PRISM after adding/changing the local font package.

## Installation

### 1. Copy the ready-to-replace source tree

Copy these folders/files over the ProjectManagement project root, preserving paths:

- `Pages/Projects/Publications/**`
- `Pages/Projects/Compendium/Index.cshtml`
- `Pages/Projects/Compendium/Index.cshtml.cs`
- `Services/Publications/**`
- `Utilities/Reporting/BrochurePdfReportBuilder.cs`
- `Utilities/Reporting/PublicationFontRegistry.cs`
- `wwwroot/css/pages/projects-publications.css`
- `wwwroot/js/pages/projects-brochure.js`
- `wwwroot/js/pages/projects-publications.js`
- `wwwroot/fonts/publications/README-PUBLICATION-FONTS.txt`
- `ProjectManagement.Tests/Publications/BrochureLayoutPlannerTests.cs`

### 2. Merge the single navigation change

Apply:

`PRISM-Publications-Navigation.patch`

or paste the exact insertion documented in:

`NAVIGATION-MERGE.txt`

This is deliberately supplied as a narrow merge rather than a full replacement for `ProjectModuleNavDefinition.cs`, because that file has independent ARPP and Industry Directory changes that should not be overwritten from an older snapshot.

### 3. Optional premium offline fonts

Install the static TTF files described in:

`wwwroot/fonts/publications/README-PUBLICATION-FONTS.txt`

This can be done before or after the code deployment. The brochure remains functional with Lato until then.

### 4. Clean/rebuild

From the application project root:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Then restart IIS/IIS Express and hard-refresh the browser.

## Acceptance checks

1. Open Projects and confirm the new **Publications** item appears before Industry directory.
2. Open Publications and confirm Overview / Brochure / Compendium are reachable from the same workspace.
3. Open Brochure and confirm Project Brief is selected by default.
4. Change the narrative source and confirm the row readiness indicator and preflight update to that source.
5. Select projects and reorder them using both drag-and-drop and the up/down controls.
6. Generate Cover A and Cover B.
7. Verify Cover B hero selection follows selected project order.
8. Verify concise project briefs are composed 4/3/2 per page according to density.
9. Verify a project over 210 words is moved to continuation feature page(s), not rendered with tiny text.
10. Generate with a missing photo and confirm the brochure still generates without inventing imagery.
11. Open the Compendium tab and generate the existing Compendium successfully.
12. Browse directly to `/Projects/Compendium` and confirm it reaches the canonical Publications/Compendium workflow.
13. If premium font files are absent, confirm the font-status card reports the Lato fallback and brochure generation still succeeds.
14. After installing the local DM Sans files and restarting, confirm the font-status card reports DM Sans ready.

## Deliberate scope boundary

This phase is schema-neutral. It does **not** yet persist named/saved brochure definitions. The user selects and orders projects for the current generation session. Adding durable named brochure presets later should be a separate migration-backed phase rather than hidden in local browser storage.

It also uses the project's configured cover photo (or the best available photo) and the existing PRISM 16:9 project-photo export pipeline. Brochure-specific focal-point/crop editing is not introduced in this phase.

## Validation performed in the preparation environment

- Both new JavaScript files pass `node --check`.
- CSS delimiter/brace checks pass.
- C# source structural delimiter checks pass.
- The brochure photo-loader contract was checked against the existing Project Briefing photo loader (`LoadAsync(projectId, photoId, ...)` returning 1600×900 JPEG content).
- The Compendium wrapper was checked against the existing `ICompendiumReadService` / `ICompendiumExportService` contracts.
- No font binary is present in the replacement package.
- No migration, `ApplicationDbContext`, `Program.cs` or existing Compendium renderer/service file is replaced by this package.

The preparation environment does not contain the .NET SDK, so the final Release `dotnet build` and `dotnet test` must be run in the normal PRISM development environment after copying the files.
