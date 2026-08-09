PRISM Project Ideas — Remarks & UX Hardening
Ready-to-paste refinement package · 09 Aug 2026

PREREQUISITE
This package is an overlay for the already-applied "Project Ideas Governance & Discussion Lifecycle" phase. Copy the files into the ProjectManagement project root and replace the matching files.

WHAT THIS PHASE FIXES
1. One authoritative remark count
   - General and Conference remain subtypes of the same ProjectIdeaComment/remark record.
   - Idea Details delete-impact UI shows one collective Remarks count.
   - Deleted Ideas recovery view shows one collective Remarks count.
   - The read model no longer carries a parallel ConferenceDirectionCount for Idea recovery summaries.
   - Conference-specific operational consumers may still filter CommentType == Conference; this does not create a second aggregate/count.

2. Discussion action continuity
   - Add/edit/delete remark POSTs return to #discussion rather than the top of the page.
   - The existing global PRISM toast surface is used instead of full-width inline success/error alerts.
   - Conference success terminology is consistent: "Conference direction added/updated/deleted."
   - General items use "Remark added/updated/deleted."

3. Details-page polish
   - Conference "Add direction" remains on one line.
   - Discussion is the dominant desktop column (approximately 40/30/30).
   - Notes heading is clarified to "Idea notes".
   - Remark action ellipsis is more discoverable without becoming visually prominent.
   - Anchored returns account for the fixed application header via scroll-margin.

4. Lifecycle hardening
   - RestoreAsync now rejects an Active/On Hold idea and only permits the Archived -> Active transition.
   - Deleted-Idea restore remains a separate governed recovery workflow.

INSTALL
1. Back up or commit the current working tree.
2. Extract this ZIP at the ProjectManagement project root and replace matching files.
3. No migration is required.
4. Run:
     node --check wwwroot/js/pages/project-ideas-details.js
     node --test wwwroot/js/projects/project-ideas-governance-contract.test.js
     dotnet build .\ProjectManagement.sln -c Release
     dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release
5. Hard-refresh the browser (Ctrl+F5).

MANUAL QA
- As Comdt, open an Idea: Conference remains the default composer type.
- Add a Conference direction: toast appears and page returns to Discussion; button text stays on one line.
- Add a General remark: Discussion count increments in the same total.
- Open Delete idea: related record summary shows only total remarks + idea notes + files; no separate Conference count.
- Open Deleted Ideas: same collective remark count is shown.
- Hover/focus a remark: the ellipsis is visible enough to discover Edit/Delete.
- Try a crafted Restore POST against an Active idea: operation is rejected; only Archived ideas can use normal Restore.
