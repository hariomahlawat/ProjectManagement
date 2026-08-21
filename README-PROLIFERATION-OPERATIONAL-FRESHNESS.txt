PRISM ERP — Proliferation Operational Freshness
Ready-to-paste implementation

PURPOSE
-------
This phase adds two distinct supervisory views to the Proliferation Overview:

1. Recent proliferation (primary business view)
   - Shows the latest APPROVED proliferation records by business chronology.
   - Detailed entries use the exact proliferation date.
   - Annual quantities are shown by year and are placed after exact dated entries in the same year.
   - Future-dated detailed entries are deliberately excluded from the "Recent proliferation" card because they have not yet occurred.
   - A subtle "Entered N days later" indicator appears only when a detailed entry was entered 30+ days after its proliferation date.

2. Data freshness (secondary supervisory view)
   - Shows the latest audit-backed staff maintenance time.
   - Shows proliferation maintenance actions in the last 30 days.
   - Shows the number of distinct active staff in the last 30 days.
   - Shows the latest five record-maintenance actions with actor, project/record context and relative time.
   - Export actions are intentionally excluded from maintenance activity.

RECORDS PAGE
------------
The Records page now has an explicit sort selector and defaults to:
- Latest proliferation

Other options:
- Latest activity
- Project A-Z
- Year

"Latest proliferation" uses the most recent approved detailed proliferation date for a project/source/year when available. Annual-only project-years use the year as lower-precision chronology. Invalid historical years are retained in the Records page but sort after valid chronological records.

FILES TO REPLACE / ADD
----------------------
Areas/ProjectOfficeReports/Application/IProliferationSummaryReadService.cs
Areas/ProjectOfficeReports/Application/ProliferationSummaryReadService.cs
Areas/ProjectOfficeReports/Proliferation/ViewModels/ProliferationSummaryModels.cs
Areas/ProjectOfficeReports/Pages/Proliferation/Summary.cshtml.cs
Areas/ProjectOfficeReports/Pages/Proliferation/Summary.cshtml
Areas/ProjectOfficeReports/Pages/Proliferation/Index.cshtml
Areas/ProjectOfficeReports/Api/ProliferationDtos.cs
Areas/ProjectOfficeReports/Api/ProliferationController.cs
wwwroot/css/proliferation.css
wwwroot/js/pages/proliferation-dashboard.js
ProjectManagement.Tests/ProliferationOperationalFreshnessTests.cs
wwwroot/js/projects/proliferation-operational-freshness.test.js

DATABASE / MIGRATION
--------------------
No database migration is required. The implementation uses existing:
- ProliferationYearlies
- ProliferationGranularEntries
- AuditLogs
- AspNetUsers
- Projects

The existing proliferation audit events already contain the fields needed for the staff-activity view.

IMPORTANT INTEGRATION NOTE
--------------------------
This package does not replace Program.cs, role policies, FFC files, Publications files, or Compendium files. It is therefore safe to apply after the previous ITO / FFC / Publications role-governance phases without reverting those changes.

VALIDATION
----------
Run after copying the files:

dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter ProliferationOperationalFreshnessTests
node --check .\wwwroot\js\pages\proliferation-dashboard.js
node --test .\wwwroot\js\projects\proliferation-operational-freshness.test.js

SMOKE TEST
----------
1. Open ProjectOfficeReports/Proliferation/Summary.
2. Confirm Recent proliferation appears directly below KPI cards.
3. Confirm newest approved dated proliferation is shown first.
4. Confirm approved future-dated detailed entries do NOT appear in Recent proliferation.
5. Confirm annual records are clearly labelled "Annual quantity" / "Annual · YYYY".
6. Confirm Data freshness shows latest maintenance time, 30-day actions, active staff and recent actions.
7. Create/update/approve a test proliferation record and refresh Overview; verify activity changes.
8. Open Records and verify default sort is Latest proliferation.
9. Switch to Latest activity, Project A-Z and Year and verify stable ordering/pagination.

DESIGN INTENT
-------------
Business chronology and data-maintenance chronology are deliberately not mixed:
- "Recent proliferation" answers: What was proliferated recently?
- "Data freshness" answers: Is the register being maintained regularly, and by whom?
