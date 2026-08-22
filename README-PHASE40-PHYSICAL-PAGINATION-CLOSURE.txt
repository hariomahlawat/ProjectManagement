PRISM COMPENDIUM — PHASE 40 PHYSICAL PAGINATION CLOSURE
=======================================================

Purpose
-------
This package fixes the production Compendium failure in which the page planner reported
67 physical pages while QuestPDF actually generated 71 pages and the verifier correctly
refused to issue the PDF.

The fix is applied against the Phase 39 Compendium generation-reliability baseline on
ProjectManagement-master (11)(20260820-074448).zip.

This is a source-only replacement package. Copy the included files over the matching paths
in the ProjectManagement root, preserving the directory structure.

NO DATABASE MIGRATION IS REQUIRED.
NO PRESET SCHEMA CHANGE IS REQUIRED.
NO ROLE / AUTHORISATION CHANGE IS INCLUDED.

Confirmed code defects fixed
----------------------------
1. Incorrect first-page physical capacity.
   The dossier planner still used a hard-coded 748 pt content envelope. The actual QuestPDF
   page reserves A4 top margin + 28 pt running header + 35 pt footer + 8 pt project top
   padding. With a 1 pt floating-point reserve, the real project body capacity is 741.89 pt.
   The old planner could therefore overbook a project page by approximately 6.11 pt before
   any text-shaping difference was considered.

2. Project title was not physically measured.
   Title allowance previously depended on character-count thresholds. Phase 40 measures the
   actual project title with bundled DM Sans SemiBold at the exact renderer font size, width
   and line height. The publication kicker is also measured, including its letter spacing.

3. Publication-section / technical-category kicker was omitted from pagination geometry.
   The main publication path now supplies the actual publication kicker used by the rendered
   dossier, including custom-section names.

4. Index pagination used abstract row units.
   Index membership is now based on measured physical heights for the index identity block,
   category heading and every project row. Long project/category names therefore consume the
   real amount of vertical space before project start pages are assigned.

5. Continuation pages used a fixed 610 pt body budget.
   Continuation capacity is now derived from the same A4/header/footer geometry as QuestPDF
   and from the physically measured continuation project-title height. Narrative,
   specifications and Additional Note sharing all consume this dynamic capacity.

6. Fit-image planner/renderer geometry disagreed.
   The planner already treated Fit images as intrinsic-height content, but the PDF compositor
   still reserved the full maximum image-frame height. Phase 40 carries source dimensions into
   the PDF model and renders Fit images at the same intrinsic height used by planning.

7. Project Particulars height measurement did not fully match the renderer.
   Panel/minimal measurements now include the real border, padding, icon/text widths,
   SemiBold font face, line height and label letter spacing used in the QuestPDF composition.

8. A planned page was still allowed to become multiple QuestPDF pages.
   Index, project and continuation page bodies are now composed as atomic ShowEntire()
   fragments. A planned physical page can no longer silently spill and shift every subsequent
   index reference.

9. Page-count mismatch diagnostics stopped too early.
   The verifier now canonicalises the rendered pages before rejecting a count mismatch and
   reports the first observable index/project drift where possible. Nearest-page resolution is
   used to avoid misleading diagnostics when the same text appears elsewhere.

10. Composition verification failure returned HTTP 400.
    A genuine planner/renderer composition conflict now returns HTTP 409 Conflict while
    preserving the Phase 39 safe error code and TraceIdentifier behaviour.

Important expected behaviour
----------------------------
Do NOT expect the corrected planner to preserve the old number "67" simply because that was
previously displayed. The old 67-page plan was based on incorrect physical assumptions.

The required invariant is:

    planned physical page count == generated PDF physical page count

The corrected plan may legitimately allocate a different number of index/continuation pages.
That is expected. The index must then use the corrected project start-page numbers.

Files included
--------------
Pages\Projects\Publications\Compendium\Index.cshtml.cs
Services\Compendiums\CompendiumDossierPaginationPlanner.cs
Services\Compendiums\CompendiumDossierTextMeasurementService.cs
Services\Compendiums\CompendiumExportService.cs
Services\Compendiums\CompendiumProjectParticularsLayoutPolicy.cs
Services\Compendiums\CompendiumReadService.cs
Utilities\Reporting\CompendiumLayoutMetrics.cs
Utilities\Reporting\CompendiumPagePlanner.cs
Utilities\Reporting\CompendiumPdfCompositionVerifier.cs
Utilities\Reporting\CompendiumPdfReportBuilder.cs
wwwroot\js\projects\publications-compendium-phase33-contract.test.js
wwwroot\js\projects\publications-compendium-phase37-1-contract.test.js
wwwroot\js\projects\publications-compendium-phase40-pagination-closure.test.js

The two historical JavaScript contract files are updated only because they asserted the old
hard-coded/syntactic implementation. Their behavioural requirements remain intact.

Validation completed in this delivery environment
-------------------------------------------------
PASS - all Compendium Node contract/regression tests: 235 passed, 0 failed
PASS - dedicated Phase 40 pagination-closure tests: 8 passed, 0 failed
PASS - changed C# delimiter/brace structural sanity checks
PASS - no trailing whitespace in replacement source files
PASS - generated Phase39 -> Phase40 patch applies cleanly and reproduces every packaged source file

The delivery environment does NOT contain the .NET SDK, so C# compilation and xUnit execution
cannot be truthfully claimed here.

Mandatory development-machine validation
----------------------------------------
Run from the ProjectManagement root after copying the files:

    dotnet build .\ProjectManagement.csproj
    dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
    node --test .\wwwroot\js\projects\publications-compendium-phase40-pagination-closure.test.js

Then run the existing Compendium JavaScript contract suite using your normal project test command.

Production smoke test
---------------------
1. Open the SAME Compendium preset that previously showed 61 selected projects and failed with
   "planner expected 67 physical pages, generated PDF contains 71".
2. Do not alter project selection/content for the first test.
3. Run Preview PDF.
4. Confirm a PDF opens instead of the page-count drift dialog.
5. Note the returned physical page count. It may differ from the old 67 because pagination is now
   physically measured.
6. Check several index page numbers against the actual dossier start pages, especially long project
   names and projects near index-page boundaries.
7. Check projects using Fit imagery, long titles, Project Particulars, technical specifications and
   Additional Notes.
8. Run final Download PDF after review requirements are satisfied.

If a residual composition problem remains, retain the new error text and TraceIdentifier. The
error now identifies the first observable index/project page drift where possible, which makes the
next diagnosis content-specific rather than speculative.

Patch
-----
The package also contains:
    prism_compendium_phase40_physical_pagination_closure.patch

This is the exact unified Phase39 -> Phase40 source delta represented by the ready-to-paste files.
