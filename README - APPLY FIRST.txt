PRISM — ARPP AoN Milestone Integrity + Word Compile Fix
======================================================

READY-TO-PASTE FILES
--------------------
Replace/add the files listed in CHANGED-FILES.txt.

This package addresses BOTH requested items:

1. CS0103 in ArppFyProjectUpdateWordBuilder.cs
2. Strict AoN completion-date semantics across shared project/report readers


1. WORD BUILDER CS0103
----------------------
Root cause:
Build() creates a local variable named resolvedOptions, but BuildDataCells() receives
the already-resolved value through its parameter named options. The listing-date
cell incorrectly referred to resolvedOptions inside BuildDataCells(), where that
identifier does not exist.

Corrected:
    options.ResolveListingDate(row)

The selected Initial Listing / Current FY Listing option therefore remains fully
functional.


2. AUTHORITATIVE AoN DATE RULE
------------------------------
AoN date is now defined strictly as:

    ProjectStage.StageCode == AON
    AND ProjectStage.Status == Completed
    AND ProjectStage.CompletedOn has a value

Only then is CompletedOn exposed as the AoN date.

Consequences:
- AoN NotStarted -> blank
- AoN InProgress -> blank
- AoN Blocked -> blank
- AoN Skipped -> blank
- Any stale CompletedOn on the above states -> ignored
- AoN Completed + date -> date
- AoN Completed + missing/backfill date -> blank

The shared ProjectFormalUpdateFactsResolver is the authoritative fix, so both
ARPP reports and briefing update-sheet consumers receive the same corrected value.


3. REPORT PREFLIGHT
-------------------
A project whose CURRENT stage is AoN is no longer incorrectly warned that its AoN
date is missing. Reaching AoN does not mean completing AoN.

AON_DATE_MISSING is raised only when:
- the project is explicitly Completed; OR
- its current lifecycle position is beyond AoN;
AND no valid completed-AoN date exists.


4. ONGOING PROJECTS READER
--------------------------
ResolveStageMilestoneDate now returns ActualCompletedOn only when the stage status
is Completed. This hardens the same milestone invariant for the existing IPA/AoN
fields used by the ongoing-projects reader.


5. REGRESSION TESTS
-------------------
Added tests covering:
- Completed AoN returns CompletedOn
- InProgress/Blocked/Skipped/NotStarted AoN with stale CompletedOn stays blank
- Completed AoN with missing/backfill date stays blank
- Valid completed AoN wins over a newer non-completed stale row
- Current AoN uses '<' rather than '<=' in report missing-date preflight
- Ongoing-project milestone reader requires StageStatus.Completed
- Word listing-date option uses the correct in-scope options variable


NO CHANGES TO
-------------
- ARPP listing-date option behaviour
- Initial vs Current FY listing resolution
- Present Stage option
- Completed-project PDC = "Completed"
- Supply Order amount/date logic
- Lifecycle sorting
- Database schema/migrations
- DI registrations
- PDF/Excel layout
- Authorisation


AFTER PASTING
-------------
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj `
    --filter "FullyQualifiedName~ProjectManagement.Tests.Reports"

Then verify one project currently at AoN:
- AoN column must be blank until AoN is explicitly completed.
- Once AoN is completed, the column must show the AoN stage CompletedOn date.
