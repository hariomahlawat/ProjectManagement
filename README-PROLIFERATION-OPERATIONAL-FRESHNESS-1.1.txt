PRISM ERP — Proliferation Operational Freshness 1.1
==================================================

Purpose
-------
This is the refinement/closure package for the Proliferation operational-freshness dashboard.
It is cumulative with the immediately preceding Operational Freshness implementation: the
package includes the Overview, Records sorting, service/model, CSS/JS and regression files
needed for the complete feature.

What this refinement fixes
--------------------------
1. Recent proliferation now represents business events rather than raw unit-level rows.
   Approved detailed records are consolidated by Project + Source + Proliferation Date.
   Quantities are summed and the Overview shows the number of detailed entries / receiving units.
   Annual quantities remain separate year-based records.

2. Data freshness now distinguishes:
   - Latest register activity: any qualifying Proliferation maintenance action.
   - Latest data entry / update: actual Create or Update of annual/detailed proliferation data.
   This prevents an approval, deletion or counting-rule change from being mistaken for fresh data entry.

3. Repetitive staff maintenance bursts are condensed in the five-line activity feed.
   Matching actions by the same actor, project and source within a 10-minute window are grouped,
   e.g. "Deleted 3 detailed entries". Raw 30-day maintenance counts remain unchanged and audit-accurate.

4. Old activity ages remain relative (e.g. "30 days ago", "3 months ago") while the exact IST
   timestamp is retained underneath. This removes the duplicate absolute timestamp seen previously.

5. Records > Latest proliferation now ignores future-dated detailed entries when selecting the
   chronology sort key, matching the Overview business-date rule.

6. Activity context can wrap to two lines and retains the full record reference in a tooltip.

Database / migrations
---------------------
No schema change. No EF migration is required.

Ready-to-paste instructions
---------------------------
Copy the contents of this folder over the ProjectManagement project root, preserving paths.
The package contains complete replacement files, not snippets.

Validation performed in this environment
----------------------------------------
- node --check wwwroot/js/pages/proliferation-dashboard.js
- node --test wwwroot/js/projects/*proliferation*.test.js
- static contract checks for grouping, data-entry freshness, future-date parity and UI labels

The .NET SDK is not installed in this execution environment, so run the following after paste:

  dotnet build .\ProjectManagement.csproj
  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter ProliferationOperationalFreshnessTests
  node --check .\wwwroot\js\pages\proliferation-dashboard.js
  node --test .\wwwroot\js\projects\*proliferation*.test.js

Recommended smoke test
----------------------
A. Create/approve 3 detailed entries for the same project/source/date but different receiving units.
   Overview should show one Recent proliferation business-event row with summed quantity and
   "3 detailed entries" / "3 receiving units".

B. Delete several old detailed entries for the same project within a few minutes.
   The activity feed should collapse them into one line, while Last 30 days keeps the raw action count.

C. Perform an approval only. Latest register activity should move forward; Latest data entry/update
   should not move forward.

D. Create or edit an annual/detailed record. Both Latest register activity and Latest data entry/update
   should move forward.

E. If an approved future-dated detailed record exists, it must not lead Overview Recent proliferation
   or Records > Latest proliferation.
