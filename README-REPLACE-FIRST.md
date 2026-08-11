# PRISM Publications — Phase 6 Visual Composition Finalisation

## Use this package when

Phase 5 is already installed and the Brochure Builder is running. This is the recommended package for the current PRISM installation shown in the acceptance screenshots.

## Installation

Copy the contents of this folder over the **ProjectManagement project root**, preserving the directory structure and replacing the matching files.

There is **no EF migration, no Program.cs change, no navigation change, no Compendium change, and no font reinstall** in Phase 6.

The replacement `BrochurePdfReportBuilder.cs` already retains the Phase 5 CS0136 compile hotfix (`featureGap` / `cardGap`).

## What Phase 6 changes

- Cover B hero is now independent from project order and from each project's normal brochure primary image.
- Automatic Cover B selection considers all usable photographs belonging to selected projects.
- Cover B has its own focal point and cover-specific 1800×1100 crop/render path.
- Final Cover B download requires explicit **Approve cover**; Preview remains available before cover approval.
- Changing Cover B title/subtitle/edition/strapline, hero or crop invalidates cover approval.
- Project reordering reruns publication preflight but no longer forces already reviewed, unchanged projects to be reviewed again.
- Project review is simplified to one **Approve project** action; it accepts the narrative and currently selected imagery together.
- Editorial image confirmation is no longer counted as a technical preflight warning.
- `SingleFeature` gets a dedicated editorial page renderer instead of an oversized full-page bordered card.
- `TwoFeature` retains alternating left/right composition and now scales image frames according to narrative length.
- Cover B hero area is increased to 364 pt and remains bottom-anchored above the closing band.
- Back cover, Cover A, fonts, Compendium and the existing layout planner thresholds are intentionally retained.
- No duplicate Project Brief detection is introduced.

## Verify after copying

From the ProjectManagement root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase6.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\projects-brochure.js
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js
```

## Acceptance checks in the browser

1. Select several projects with more than one available photograph.
2. Verify **Cover B hero → Change hero** lists photographs independently of project primary-image selection.
3. Select a different cover photograph and adjust its crop. Confirm that project-card imagery remains unchanged.
4. Preview should be enabled after technical preflight even before final editorial approval.
5. Final Download should require all selected projects to be approved and, for Cover B, the cover to be approved.
6. Reorder an already reviewed project. Its project approval should remain intact.
7. Change a project's brief/photo/crop. Only that project's review should be invalidated.
8. Generate one 190–200 word project and confirm SingleFeature has a large intentional image + narrative composition with no giant empty bordered rectangle.
9. Generate two projects at ≤125, 126–155 and 156–185 words and confirm the TwoFeature photograph size responds to copy length while body type remains readable.
10. Generate both Cover A and Cover B PDFs and compare against the reference brochure.

## Validation performed in the preparation environment

- `node --check wwwroot/js/pages/projects-brochure.js` — passed.
- `node --test wwwroot/js/projects/publications-brochure-contract.test.js` — **14/14 passed**.
- Modified C# files passed structural delimiter checks.
- Publications CSS passed structural brace validation.
- Incremental changes were diffed against the Phase 5 baseline with the compile hotfix applied.

The preparation environment does not contain the .NET SDK, so `dotnet build` and xUnit execution must be completed in the user's normal PRISM development environment.
