PRISM PUBLICATIONS — PHASE 9
MEASURED HARD-COPY COMPOSITION & COVER A FIDELITY
==================================================

PURPOSE
-------
Phase 9 replaces the remaining compact-print word-count heuristics with font-aware,
width-aware measurement using the installed offline DM Sans files and SkiaSharp.
The measured geometry is shared by preflight, sheet planning and the QuestPDF
hard-copy compositor.

This package assumes Phase 8 is already installed. It is ready to copy over the
ProjectManagement project root while preserving paths.

CORE IMPLEMENTATION
-------------------
1. Font-aware print measurement
   - New IBrochurePrintMeasurementService / BrochurePrintMeasurementService.
   - Measures actual glyph widths with SkiaSharp.
   - Uses the same local DM Sans Regular/SemiBold files used by the publication stack
     when available; platform fallback is used only if the offline font is unavailable.
   - Measures project titles, Project Brief wrapping, Gallery 2 geometry, closing
     institutional matter and the first-sheet institutional composition.

2. Measured sheet planner
   - New IBrochurePrintPagePlanner / BrochurePrintPagePlanner.
   - Project order is never changed.
   - Up to four projects per normal hard-copy sheet when measured geometry permits.
   - Each project has controlled Visual / Balanced / Compact candidates rather than
     arbitrary free-form layout.
   - Page count is minimised first; then the planner favours good utilisation and the
     highest-quality legal variant.
   - The exact measured closing block is reserved on the final sheet.

3. Cover A measured composition
   - The first sheet is composed as one measured vertical system.
   - Hero/artwork, Centre of Expertise statement, institutional narrative,
     future-readiness text, Procurement, contacts and strapline are measured together.
   - The Phase 8 fixed body/contact spacer geometry is removed.
   - Body typography now targets 9 pt and never goes below the Phase 9 8.4 pt floor.
   - Approved institutional artwork remains preferred; the controlled PRISM
     institutional fallback remains available and never substitutes a random project photo.

4. Measurement-based print plan
   - Preflight now returns a per-sheet plan.
   - Builder UI shows planned sheet count, average fill, lowest project-sheet fill,
     final-sheet fill and a sheet map such as:
       1. Institutional front page
       2. Projects 1–4
       3. Projects 5–8
       4. Projects 9–11 + closing
   - Values use the same measured model consumed by the renderer.

5. Profile-aware cover defaults
   - Until the user explicitly chooses a cover in the current composition session:
       Print / Compact -> Cover A (Institutional)
       Digital / Comfortable -> Cover B (Contemporary)
   - Explicit user cover choice is never overwritten by later unrelated changes.

6. Existing hard-copy visual grammar retained
   - Original brochure dimensions remain 423.23 x 846.755 pt.
   - Centred dark-green project headings remain.
   - Justified Project Briefs remain.
   - Single / Automatic / Gallery 2 image treatment remains.
   - Cover B and Digital / Comfortable remain isolated from the measured Print planner.

NO DATABASE CHANGE
------------------
Phase 9 has:
- no EF migration;
- no Program.cs merge;
- no navigation change;
- no Compendium change;
- no font reinstall.

PublicationServiceCollectionExtensions.cs IS replaced because it registers the two new
measured-print services through the existing AddProjectPublications() integration point.
Program.cs already calls that extension from the previous phases.

INSTALL
-------
1. Copy this package over the ProjectManagement project root.
2. Preserve directories and replace matching files.
3. From the ProjectManagement root run:

   Set-ExecutionPolicy -Scope Process Bypass
   .\tools\Test-PrismPublicationsPhase9.ps1

   Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

   dotnet restore .\ProjectManagement.csproj
   dotnet build .\ProjectManagement.csproj
   dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

   node --check .\wwwroot\js\pages\projects-brochure.js
   node --test .\wwwroot\js\projects\publications-brochure-contract.test.js

ACCEPTANCE TEST
---------------
After a successful build:

A. Generate Print / Compact with Cover A using the same 8-project test set.
   Check:
   - first page no longer has the previous large institutional-copy/contact void;
   - body copy is visibly larger than Phase 8;
   - contacts follow the institutional copy naturally;
   - no text clipping.

B. Generate 12–15 representative projects with realistic 100–150 word Project Briefs.
   Check the Print Plan before Preview:
   - four-project sheets should occur where measured geometry actually permits;
   - project order must match the selected order;
   - final sheet should normally combine projects + closing matter;
   - low-fill sheets should correspond to a genuine next-project fit constraint.

C. Compare page 1, one normal project sheet and the final sheet against the approved
   physical brochure before treating the Print / Compact renderer as frozen.

IMPORTANT
---------
The preparation environment used to create this package does not expose the .NET SDK.
JavaScript and structural/source checks have been executed here, but dotnet build/test must
be run on the PRISM development machine before deployment.
