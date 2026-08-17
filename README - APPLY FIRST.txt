PRISM FFC Projects Update — Control & Export Cleanup
=====================================================

This is a focused follow-up to the FFC Projects Update implementation.

FIX 1 — OVERALL STATUS CONTROL
------------------------------
Previous behaviour:
- Overall status relied on an onchange="this.form.submit()" auto-submit.
- On the observed runtime screen the checkbox changed visually but the report
  did not refresh, leaving the six-column table unchanged.
- There was no explicit report-update action for the user.

New behaviour:
- Overall status is a normal report option.
- The toolbar now has an explicit "Update report" button.
- Tick/untick Overall status, then press Update report.
- Country-year selection is synchronized on every form submit.
- The Country/Year dropdown's Apply button is also a real form submit.
- Direct JavaScript form.submit() calls have been removed.

This makes the workflow deterministic and visible:
    Change option -> Update report -> browser preview refreshes
    -> Word/PDF/Excel links carry the resolved IncludeOverallStatus state.


FIX 2 — COUNTRY CODES REMOVED FROM FORMAL OUTPUT
------------------------------------------------
Three-letter country codes such as FRA / ETH / MMR / NGA / LKA / KHM are no
longer rendered in:
- Browser formal preview
- Word export
- PDF export
- Excel export

Formal group headings are now simply:
    France – 2026
    Ethiopia – 2025
    Myanmar – 2025
    ...

The Country/Year selector may still show the code as secondary UI metadata;
it is not part of the report output.


FILES TO REPLACE
----------------
See CHANGED-FILES.txt.


AFTER PASTING
-------------
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj `
    --filter "FullyQualifiedName~FfcProjectsUpdate"

node --check .\wwwroot\js\pages\projects-reports-ffc.js


MANUAL ACCEPTANCE
-----------------
1. Open FFC Projects Update.
2. Tick Overall status.
3. Click Update report.
4. Confirm the URL/reloaded state has IncludeOverallStatus=true and the seventh
   "Overall status" column appears.
5. Untick it and click Update report; the column must disappear.
6. Change Country/Year selection and Apply; selection must remain correct.
7. Export Word/PDF/Excel with Overall status both OFF and ON.
8. Confirm no three-letter country code appears in any formal output.
