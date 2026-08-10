# PRISM Publications — Phase 4 Ready-to-Replace Update

## Use this package when

Phase 3 is already installed and the current Publications/Brochure pages open successfully, as shown in the latest PRISM screenshots.

Copy the contents of this folder over the **ProjectManagement project root**, preserving relative paths and replacing matching files.

There is **no Program.cs replacement, no navigation merge and no database migration** in this incremental phase.

## What Phase 4 fixes/improves

- Rebuilds the Brochure CSS against the actual current Razor/JavaScript markup and removes obsolete Phase 1 selector drift.
- Corrects the malformed duplicate photograph CSS rule.
- Makes project filters compact and the portfolio table internally scrollable with sticky headings.
- Adds live matching/selected counts, readiness filtering and Selected-only filtering.
- Turns Select visible into a counted Select/Deselect action with the 100-project limit respected.
- Replaces fixed Project Photo derivative URLs with a dedicated publication-photo endpoint.
- Makes Brochure thumbnails, focal editing, server preflight and final PDF rendering resolve from the same master/fallback source pipeline.
- Uses source-faithful focal coordinates and an accurate 16:9 crop-frame overlay.
- Classifies actual publication-source image quality and applies stricter quality requirements to Cover B hero / feature frames than standard project cards.
- Adds in-memory photo-probe caching keyed by photo/version and file metadata.
- Makes `AddProjectPublications()` self-register the memory cache it now depends on.
- Keeps second imagery an explicit editorial choice; Gallery 2 does not silently select another project photo.
- Makes server preflight actionable and expandable rather than truncating findings.
- Adds direct actions to locate the project, open Project Brief and manage/configure photographs.
- Hides healthy DM Sans implementation status from normal users and shows only a fallback warning when required.
- Removes normal-user QuestPDF/server-path terminology.
- Compacts Publication Settings and moves optional controls into Advanced publication settings.
- Reduces Publications landing artwork height.
- Splits long institutional introduction copy across readable pages without reducing the 11 pt body size.
- Adds JS/Razor/CSS contract tests, photo-quality tests and introduction-pagination tests.

## Install

1. Close/restart the running debug instance after copying, or stop IIS Express first.
2. Copy this folder's application files into the ProjectManagement root, preserving paths.
3. Clean and rebuild.
4. Run tests.
5. Restart IIS/IIS Express and hard-refresh the browser.

Recommended commands:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase4.ps1

Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\projects-brochure.js
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js
```

## Acceptance checks

1. Open Projects > Publications > Brochure.
2. Confirm Advanced publication settings is collapsed by default and the main settings block is visibly shorter.
3. Confirm search + filters form a compact desktop toolbar.
4. Confirm the project register scrolls internally and its headings remain sticky.
5. Change Narrative Source and test Brochure ready / Missing selected narrative / Missing photograph / Selected only.
6. Confirm matching/selected counts and Select/Deselect visible labels update accurately.
7. Confirm photographs display a designed fallback instead of the browser broken-image icon when a preview is unavailable.
8. Open Images for a selected project. The focal view should show the uncropped publication source with a 16:9 crop frame.
9. Confirm Gallery 2 requires an explicit second photograph.
10. Run preflight and use Show all findings plus Locate / Open project brief / Configure image / Manage photos.
11. Confirm healthy DM Sans does not occupy a sidebar card.
12. Preview Cover B with a lower-resolution photograph and confirm placement-specific softness warning appears.
13. Generate a long introduction and confirm it continues to additional introduction pages instead of using tiny text.
14. Preview and Download the same selection and confirm the composition is identical.
15. Open Compendium and confirm the existing workflow remains unaffected.

## Scope boundary

No saved/named brochure records or publication history are introduced here. That remains a separate migration-backed phase after real generated brochures have been reviewed against the Canva reference.
