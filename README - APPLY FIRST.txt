PRISM FFC Projects Update — Presentation Hardening
=================================================

This package is a focused refinement over the current working FFC report.

IMPLEMENTED
-----------

1. BROWSER LONG-REGISTER USABILITY
   - The KPI command strip is no longer sticky on the FFC report.
   - On wide workstations (>= 1500px), the actual FFC column header is sticky
     below the PRISM module navigation.
   - The wide-screen table no longer sits inside an overflow container that
     would prevent viewport sticky positioning.
   - Smaller screens retain the existing horizontal-scroll fallback.
   - The Overall-status table minimum width is reduced modestly from 1580px
     to 1500px without changing its column allocations, making the full
     register more likely to fit naturally on wide displays.

2. PDF OVERALL STATUS
   - Overall status is now a real country-year RowSpan cell.
   - It no longer sits only in the first project row followed by unrelated
     blank cells.
   - This matches the browser/Word/Excel meaning: one Overall status belongs
     to the entire Country-Year group.

3. PDF PAGINATION / ORPHAN PAGE HARDENING
   - The RowSpan change removes the artificial first-row height inflation
     caused by long Overall-status text.
   - A4 landscape vertical margins are reduced from 22pt to 16pt.
   - Header/footer and row padding are tightened slightly.
   - Body/header font sizes are NOT reduced.
   - Project and Status receive slightly more horizontal space.
   - These changes are intended to prevent a one-row orphan second page for
     the current 19-project production dataset while remaining safe for
     genuinely larger multi-page reports.

4. WORD HEADER GEOMETRY
   - S. No. widened.
   - Quantity widened.
   - Project and Status rebalanced.
   - S. No., Cost, Quantity and Status headings receive OpenXML NoWrap.
   - Both 6-column and 7-column variants remain exactly 15,700 twips wide.

5. EXCEL COUNTRY-YEAR GROUPING
   - Country-Year group headings are explicitly left-aligned, matching the
     browser, Word and PDF register style.

6. REGRESSION TESTS
   - Excel left-alignment.
   - Word NoWrap markers.
   - PDF country-year RowSpan.
   - FFC sticky-header / non-sticky-KPI browser contract.


PRESERVED
---------
- Country-Year default selection rule.
- Manual inclusion of all-installed groups.
- Explicit Update report workflow.
- Overall status optional column.
- No 3-letter country codes in formal outputs.
- Word/PDF/Excel export routes and data source.
- Existing IFfcQueryService reuse.
- Existing authorization.
- No database migration.
- No DI registration change.


FILES TO REPLACE
----------------
See CHANGED-FILES.txt.


AFTER PASTING
-------------
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj `
    --filter "FullyQualifiedName~FfcProjectsUpdate"


MANUAL ACCEPTANCE
-----------------
1. Enable Overall status and Update report.
2. Scroll through the browser report on the wide workstation:
   - KPI strip must scroll away.
   - Column headings must remain visible below Projects navigation.
3. Export PDF:
   - Overall status must visually span each Country-Year group.
   - Check whether the current 19-project dataset remains on one page.
   - If future data genuinely requires more pages, headers must repeat normally.
4. Export Word:
   - S. No. and Quantity headings should stay on one line.
5. Export Excel:
   - Country-Year group rows must be left aligned.
6. Confirm no FRA / ETH / MMR / etc. appears in formal outputs.
