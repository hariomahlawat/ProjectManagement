PRISM PUBLICATIONS — PHASE 8
HARD-COPY PAGINATION & OFFICIAL-STYLE PRINT FIDELITY
====================================================

PURPOSE
-------
Phase 8 builds on the Phase 7 Print / Compact profile and addresses the remaining
hard-copy brochure differences identified against the approved reference brochure.

The interior project-module concept is retained. The implementation focuses on
sheet planning, first/final-page fidelity, print preflight and editorial usability.

WHAT CHANGED
------------
1. Closing-aware compact-sheet planner
   - New BrochurePrintCompactPlanner.
   - Page membership is determined before QuestPDF renders the sheets.
   - The final project sheet reserves space for:
       Visionary Horizons & Strategic Objectives
       New Simulators guidance
       strapline
   - Closing matter normally shares the final sheet with projects instead of
     creating a mostly-empty dedicated page.
   - Project order is never changed by the planner.
   - Modest per-module expansion uses residual sheet height without inflating
     the brochure into a spacious digital layout.

2. Print / Compact Cover A
   - Dedicated institutional treatment.
   - Approved institutional artwork is used when present.
   - If artwork is absent, PRISM renders a disciplined institutional fallback.
   - It no longer substitutes the first selected project's photograph.
   - Opening narrative, future-readiness copy, procurement guidance and contact
     information remain content-bearing on the first sheet.
   - Cover B remains the contemporary image-led alternative.

3. Hard-copy project typography
   - Project headings are centred in the green title band.
   - Project narrative is justified for a denser print-publication treatment.
   - Existing Single / Automatic / Gallery 2 image handling is retained.

4. Direct Gallery 2 control in Publication Review
   - Image treatment is now selectable directly while reviewing each project.
   - Choosing Gallery 2 without a second image opens the second-image chooser.
   - A project cannot be approved in Gallery 2 mode until the second image is set.

5. Authoritative institutional preflight
   - First/final-page publication text now goes through the same server-side
     preflight used for project narratives and photographs.
   - Missing or overlength institutional sections are blockers in the visible
     Publication preflight panel, not late Preview-only errors.

6. Print fit feedback
   - Live word counters against compact-print section limits.
   - Restore approved text action.
   - Preflight shows:
       estimated page count
       estimated average sheet fill
       final-sheet packing state

INSTALL
-------
This incremental package assumes the Phase 7 brochure implementation and its
build hotfix are already installed.

1. Stop PRISM / IIS Express if desired.
2. Copy the contents of this package over the ProjectManagement project root.
3. Preserve the directory structure and replace matching files.
4. No EF migration is required.
5. No Program.cs change is required for Phase 8.
6. No publication font reinstall is required.

Then run:

    Set-ExecutionPolicy -Scope Process Bypass
    .\tools\Test-PrismPublicationsPhase8.ps1

    Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

    dotnet restore .\ProjectManagement.csproj
    dotnet build .\ProjectManagement.csproj
    dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

    node --check .\wwwroot\js\pages\projects-brochure.js
    node --test .\wwwroot\js\projects\publications-brochure-contract.test.js

ACCEPTANCE CHECK
----------------
Use 8–12 representative projects and Print / Compact.

Verify:
- Publication preflight shows an estimated page count and average fill.
- Final sheet reports one or more projects + closing matter where feasible.
- Preview does not produce a mostly-empty closing-only page for a normal set of
  medium-length Project Briefs.
- Cover A uses institutional artwork/fallback, not an arbitrary project photo.
- Cover B still uses the independently approved hero.
- Project title bands are centred.
- Project paragraphs are compact/justified.
- Gallery 2 can be selected directly during Review.
- Clearing or over-extending institutional copy immediately appears as a preflight blocker.
- Restore approved text returns all hard-copy institutional fields to the reference wording.

IMPORTANT
---------
The planner is deliberately deterministic and order-preserving. It optimises page
breaks; it does not reorder projects.

The exact PDF layout still depends on the installed publication font metrics and
the actual selected photographs. Always review the generated hard-copy PDF before
final distribution.
