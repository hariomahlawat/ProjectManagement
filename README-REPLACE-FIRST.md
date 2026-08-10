# PRISM Project Publications — Phase 2 Quality Hardening

## Scope

This package upgrades the existing Project Publications / Capability Brochure implementation from a functional Phase 1 builder to a publication-quality workflow. It does **not** replace the existing Simulators Compendium and does **not** add a database migration.

Phase 1 must already be present. If it is not, use the consolidated package supplied alongside this incremental package.

## What Phase 2 implements

### 1. Proper service architecture
- `IBrochurePublicationService` owns brochure preparation/preflight/build orchestration.
- `IBrochurePhotoService` owns publication photo probing and rendering.
- `IBrochurePdfReportBuilder` owns PDF composition.
- `IPublicationFontService` owns publication fonts.
- `AddProjectPublications()` centralises DI registration.
- The Razor Page no longer constructs publication services manually.

### 2. Startup font initialisation
- Publication fonts are registered during application startup through a hosted warm-up service.
- Existing `wwwroot/fonts/publications` DM Sans / Alatsi installation remains fully supported.
- An optional server-resource location `Resources/Publications/Fonts` is also recognised when that folder is included in deployment.
- QuestPDF Lato remains a deterministic fallback.

### 3. Authoritative server preflight
Preview and final generation now use the same server preparation rules. Preflight validates:
- project still exists and is publishable;
- selected narrative exists;
- selected primary/secondary photos belong to the project;
- selected photo files actually exist and can be decoded;
- publication image resolution;
- Gallery 2 has a valid second photo;
- explicit text-only publication policy;
- selection limit;
- exceptional narrative length.

Findings are classified as **Blocker**, **Warning**, or **Information**. Preview and Generate are enabled only after a current blocker-free server preflight.

### 4. Brochure-specific image editorial control
Each selected project can now have independent brochure image settings without modifying the project's normal PRISM cover photo:
- image treatment: Automatic / Single / Gallery 2;
- explicit primary photo selection;
- optional explicit second photo selection;
- independent focal point for each selected photo;
- click-to-position focal crop preview;
- focal-point reset.

Secondary imagery is deliberately opt-in. PRISM does not silently select and warn about a second photograph the user never chose. Selecting Gallery 2 prompts the user to choose a second image.

### 5. Publication photo pipeline
The brochure no longer uses the PowerPoint loader's fixed centre crop. It:
- prefers the preserved project photo master;
- falls back through the largest available derivatives;
- honours the user-selected focal point;
- creates a deterministic 16:9 publication crop;
- renders a 1920×1080 JPEG for the PDF composer;
- performs actual-file probing before export.

This is isolated to brochure generation and does not alter stored project photographs.

### 6. Publication-aware page planning
The page planner now uses dynamic programming instead of greedy first-fit layout.
- 4 concise projects can still use a four-card page.
- 5 concise projects prefer 3+2 instead of 4+1.
- 6 concise projects can balance as 3+3.
- Gallery 2 projects cannot be forced into 3/4-card pages.
- exceptional long narratives use feature continuation pages.
- continuation chunks are balanced; 211 words becomes 106+105 rather than 210+1.
- selected project order remains authoritative.

### 7. Exact PDF preview
`Preview PDF` and `Generate brochure PDF` use the **same data preparation and same QuestPDF composer**. Preview is returned inline in a new browser tab; final generation is returned as an attachment.

### 8. Cover improvements
- Cover B uses the focal-cropped first usable selected project image as its hero.
- Cover A supports an approved local institutional artwork asset.
- If Cover A artwork is absent, the existing disciplined project montage remains the fallback.

Optional approved artwork paths:
- `wwwroot/img/publications/cover-a-institutional.jpg`
- `wwwroot/img/publications/cover-a-institutional.png`
- `wwwroot/img/publications/cover-a-institutional.webp`

See `wwwroot/img/publications/README-COVER-A.txt`.

## Installation — incremental Phase 2

Copy these replacement/new paths over the ProjectManagement project root:

- `Pages/Projects/Publications/Brochure/Index.cshtml`
- `Pages/Projects/Publications/Brochure/Index.cshtml.cs`
- `Services/Publications/BrochureContracts.cs`
- `Services/Publications/BrochureLayoutPlanner.cs`
- `Services/Publications/BrochurePhotoService.cs`
- `Services/Publications/BrochurePublicationService.cs`
- `Services/Publications/PublicationServiceCollectionExtensions.cs`
- `Utilities/Reporting/BrochurePdfReportBuilder.cs`
- `Utilities/Reporting/PublicationFontRegistry.cs`
- `wwwroot/css/pages/projects-publications.css`
- `wwwroot/js/pages/projects-brochure.js`
- `wwwroot/fonts/publications/README-PUBLICATION-FONTS.txt`
- `wwwroot/img/publications/README-COVER-A.txt`
- `ProjectManagement.Tests/Publications/BrochureLayoutPlannerTests.cs`
- `ProjectManagement.Tests/Publications/BrochurePhotoCropTests.cs`
- `ProjectManagement.Tests/Publications/BrochurePdfReportBuilderTests.cs`

Then apply the narrow Program merge:

```powershell
git apply .\PRISM-Publications-Phase2-Program.patch
```

or use the exact manual insertion in `PROGRAM-MERGE.txt`.

There is no navigation change in Phase 2; the Phase 1 **Publications** navigation item remains as-is.

## Fonts

If you already ran the previously supplied `Install-PrismPublicationFonts.ps1`, no further font installation is required. Phase 2 continues to recognise:

```text
wwwroot\fonts\publications\dm-sans\
wwwroot\fonts\publications\alatsi\
```

Restart the PRISM application pool after adding or changing font files.

## Build

From the ProjectManagement project root:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Then restart IIS / IIS Express and hard-refresh the browser.

## Acceptance checks

1. Open **Projects → Publications → Brochure**.
2. Confirm Project Brief remains the default narrative source.
3. Select a project and open **Images**.
4. Change the primary photograph and click different positions on the focal preview; verify the marker moves.
5. Select **Gallery 2** and verify a distinct second photograph is required.
6. Select **Single** and verify the second-image treatment is not used.
7. Confirm server preflight shows Blockers / Warnings / Info and actually detects an unavailable selected image file.
8. Confirm Preview and Generate remain disabled while preflight is stale/running or has blockers.
9. Generate Preview PDF; verify it opens inline and matches the final PDF layout.
10. Verify 5 concise projects are composed as 3+2 rather than 4+1.
11. Verify a 211-word narrative creates balanced continuation pages rather than a one-word orphan page.
12. Verify Cover B uses the selected focal crop for its hero image.
13. Add approved Cover A artwork and verify it replaces the montage; remove it and confirm montage fallback.
14. Verify DM Sans is reported ready when the local static TTF package is present; otherwise Lato fallback remains operational.
15. Open Publications → Compendium and verify the existing Compendium remains unchanged and generates normally.

## Deliberate scope boundary

This phase still does **not** persist named brochure definitions, drafts, editions or project-specific brochure selections in the database. That should be the next schema-backed phase after the rendering/preview workflow has been visually accepted.

No database migration is included in this package.
