PRISM ERP — FFC Projects Update Report
======================================

READY-TO-PASTE IMPLEMENTATION
-----------------------------
This package adds the second report under:

    Projects -> Reports -> FFC Projects Update

The report reuses the existing authoritative IFfcQueryService used by the
FFC Detailed Table. No duplicate FFC data query or database migration is introduced.


BUSINESS RULES IMPLEMENTED
--------------------------
1. Report grouping
   - Country – Year, matching the FFC Detailed Table.

2. Default country-year selection
   - A country-year is EXCLUDED BY DEFAULT only when EVERY project in that
     country-year has Status exactly "Installed".
   - Such groups remain visible in the selector and can be manually included.
   - Mixed groups (for example Installed + Planned) remain selected by default.

3. Manual country-year selection
   - User can choose any combination of country-year groups.
   - Controls include:
       Default active
       Select all
       Clear
       Apply
   - An explicit empty custom selection remains empty; it does not silently
     revert to defaults.

4. Columns
   Default:
       S. No.
       Project
       Cost (₹ lakh)
       Quantity
       Status
       Current progress

   Optional:
       Overall status

5. Output parity
   The same selected country-years and Overall status option are used by:
       Browser preview
       Word
       PDF
       Excel

6. Exports
   - A4 landscape Word
   - A4 landscape PDF
   - Excel working copy
   - File naming: FFC_Projects_Update_yyyyMMdd_HHmm.*

7. Current FFC logic retained
   - Project name/cost/progress/status all come from IFfcQueryService.
   - Cost remains the same resolved FFC Detailed Table cost in ₹ lakh.
   - FFC status remains the existing bucket label.
   - Overall status remains FfcRecord.OverallRemarks.


AUTHORIZATION
-------------
This implementation deliberately preserves the CURRENT Projects -> Reports
authorization contract:

    ProjectOfficeReportsPolicies.ViewArpp

No navigation-policy or role change is made in this phase.


NO PROGRAM.CS CHANGE
--------------------
No new DI registration is required.

The new Razor Page injects the already-registered IFfcQueryService.
The report factory and export builders are deterministic/static report components.
This avoids replacing Program.cs and reduces deployment/regression risk.


FILES
-----
See CHANGED-FILES.txt.

Existing files replaced:
    Pages/Projects/Reports/Index.cshtml
    wwwroot/css/pages/projects-reports.css

New files:
    Pages/Projects/Reports/FfcProjectsUpdate.cshtml
    Pages/Projects/Reports/FfcProjectsUpdate.cshtml.cs
    Services/Reports/FfcProjectsUpdate/*
    wwwroot/js/pages/projects-reports-ffc.js
    ProjectManagement.Tests/Reports/FfcProjectsUpdate*


AFTER PASTING
-------------
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj `
    --filter "FullyQualifiedName~FfcProjectsUpdate"

node --check .\wwwroot\js\pages\projects-reports-ffc.js


MANUAL ACCEPTANCE CHECK
-----------------------
1. Open Projects -> Reports.
2. Confirm a second report row: FFC Projects Update.
3. Open it.
4. Verify every country-year is available in the selector.
5. Verify all-installed country-years are unchecked by default.
6. Verify mixed/non-installed country-years are checked by default.
7. Select an all-installed group manually and Apply; it must appear.
8. Toggle Overall status; preview must add/remove the final column.
9. Export Word/PDF/Excel in both modes.
10. Verify selected country-years and column set match the browser preview.
