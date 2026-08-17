PRISM ARPP FY Project Update — Production Hardening
==================================================

READY-TO-PASTE REPLACEMENTS
---------------------------
Replace these files using the same relative paths:

1. Pages\Projects\Reports\ArppFyUpdate.cshtml
2. wwwroot\css\pages\projects-reports.css
3. Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdateContracts.cs
4. Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdateWordBuilder.cs
5. Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdatePdfBuilder.cs
6. Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdateExcelBuilder.cs
7. ProjectManagement.Tests\Reports\ArppFyProjectUpdateExportTests.cs
8. ProjectManagement.Tests\Reports\ArppFyProjectUpdatePresentationContractTests.cs

WHAT THIS FIXES
---------------
A. Production ARPP / PPP identifiers
- Removes the browser nowrap rule that caused real production ARPP numbers to overlap Project Name.
- Widens the browser ARPP No. column from 5.1rem to 8.8rem.
- Inserts safe <wbr> opportunities after '/' while HTML-encoding the original identifier.
- Retains overflow-wrap:anywhere as a defensive fallback for unusually long uninterrupted identifiers.
- Raises formal web-table minimum width to 1540px (1670px with Present Stage), preserving horizontal scroll only on smaller screens.
- Rebalances ARPP No. width in Word, PDF and Excel using real production-length identifiers.

B. Completed projects in PDC dt
- Adds an authoritative IsCompleted report-row property based only on ProjectLifecycleStatus.Completed.
- PDC dt now displays 'Completed' for completed projects in browser, Word, PDF and Excel.
- Historical Development PDC values are deliberately ignored for completed projects.
- Non-completed projects continue to show the resolved Development PDC date when applicable, otherwise blank in formal exports / dash in browser preview.

C. Regression protection
- Adds production-style long-identifier tests.
- Verifies Completed overrides a historical Development PDC.
- Verifies Excel standard and Present Stage layouts both place Completed in the correct PDC cell.
- Preserves the existing PDF ligature/text-integrity safeguard and all Present Stage behavior.

NOT CHANGED
-----------
- ARPP membership / current-position resolution
- First ARPP listing date resolution
- Lifecycle ordering
- Supply Order value logic
- Present Stage option behavior
- Authorization / DI / database schema

NOTE ON REPEATED 31 DEC 2024 LISTING DATES
------------------------------------------
No automatic change has been made. The report currently uses the authoritative first published ARPP IssueDate.
If many projects show 31 Dec 2024, that value should be verified in the published ARPP source records before changing report logic; the report should not fabricate a different date.

AFTER PASTING
-------------
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~ProjectManagement.Tests.Reports"

Then regenerate the production report and verify a long ARPP number and a completed project in browser, Word, PDF and Excel.
