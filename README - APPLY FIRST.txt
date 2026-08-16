PRISM Reports Workspace Refinement + Export Standardisation
============================================================

This package is ready to paste over the current ARPP FY Project Update implementation.
It contains only files changed in this refinement phase.

WHAT THIS PHASE DOES
--------------------
1. Makes both Projects > Reports pages use the uncapped PRISM workspace shell.
2. Removes the inner 760/1680px presentation constraints and duplicate page gutters.
3. Converts the Reports landing card into a full-width catalogue row.
4. Compacts the ARPP report workspace header and places FY + Word/PDF/Excel controls together.
5. Keeps the report summary compact and correctly sticky below the 52px global header + 46px Projects subnav.
6. Renames "Publication preflight" to "Report preflight" and keeps normal warnings collapsed by default.
7. Removes the 260px nested preflight scroller; expanded warnings use normal page scrolling.
8. Reduces the formal table minimum width from 1540px to 1450px and makes Project/Remarks consume spare wide-screen space.
9. Increases CFA width so real CFA names wrap materially less.
10. Preserves horizontal scrolling only as a smaller-screen fallback.
11. Standardises the formal preview, Word, PDF and Excel heading through one report property:

    PROJECT UPDATE : ARPP LISTED PROJECTS (FY 2026-27)

    The FY is generated dynamically.
12. Word/PDF now use the heading as one line; Excel merges it across the complete report width on row 1.
13. Removes the old split export headings and all "ARPP APPROVED PROJECTS" export wording.
14. Includes regression tests for workspace-shell, preflight, width and formal-heading contracts.

FILES TO REPLACE
----------------
Pages\Projects\Reports\Index.cshtml
Pages\Projects\Reports\ArppFyUpdate.cshtml
wwwroot\css\pages\projects-reports.css
Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdateContracts.cs
Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdateWordBuilder.cs
Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdatePdfBuilder.cs
Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdateExcelBuilder.cs
ProjectManagement.Tests\Reports\ArppFyProjectUpdateExportTests.cs

NEW TEST FILE
-------------
ProjectManagement.Tests\Reports\ArppFyProjectUpdatePresentationContractTests.cs

NOT CHANGED
-----------
- No database migration.
- No Program.cs / DI change.
- No ARPP current-position business logic change.
- No stage-ordering rule change.
- No report PageModel change.
- Completed projects remain classified directly from lifecycle status and do not consult stage history.
- SO amount remains PNC cost, falling back only to L1 cost.

IMPORTANT
---------
ArppFyProjectUpdateWordBuilder.cs in this package also preserves the previous OpenXML CS1736 fix:
optional JustificationValues parameters use nullable defaults and resolve Left inside the method.

VALIDATION
----------
The environment used to prepare this package does not contain the .NET SDK, so a real dotnet build/test could not be executed here.
Static contract checks passed for:
- balanced source/CSS braces;
- workspace flags on both Razor pages;
- exact shared formal title;
- Word/PDF/Excel use of report.FormalTitle;
- removal of the 760px catalogue cap;
- removal of the 260px nested preflight height;
- 1450px formal-table fallback width;
- Word table widths still sum to 16000 dxa.

Recommended local validation:

  dotnet build .\ProjectManagement.csproj
  dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~ProjectManagement.Tests.Reports"
