PRISM ERP — DEPENDENCY-AWARE STAGE CHRONOLOGY FIX
Ready-to-paste replacement package
Date: 28 Aug 2026

PURPOSE
-------
This package corrects the lifecycle chronology defect in which display order was
being treated as dependency order. In particular, Benchmarking (BM) no longer
waits for Technical Evaluation (TEC) merely because TEC is displayed before BM.
The configured workflow dependency graph is now authoritative for chronology.

EXPECTED V2 BEHAVIOUR
---------------------
BID -> TEC ----\
                -> COB
BID -> BM  ----/

Therefore:
- TEC depends on BID.
- BM depends on BID.
- TEC and BM may overlap.
- COB depends on BOTH TEC and BM.
- COB's earliest start is the later effective completion date of TEC and BM.
- Same-day commencement remains permitted.
- The following day remains the suggested start date.

PRODUCTION FILES TO REPLACE
---------------------------
1. Services\Stages\StageDateSuggestionResolver.cs
2. Services\Stages\StageRequestService.cs
3. Services\Stages\ProjectStageWorkflowPolicy.cs
4. Services\Approvals\StageApprovalSequenceService.cs
5. Services\Projects\ProjectTimelineReadService.cs

TEST FILES INCLUDED
-------------------
The ProjectManagement.Tests folder contains the regression tests and the two
existing tests whose ProjectTimelineReadService constructor call required the
new workflow-policy dependency.

HOW TO APPLY
------------
1. Back up your current source tree or commit it to Git.
2. Copy the contents of this package into the ProjectManagement project root.
3. Allow Windows to merge folders and REPLACE files with the same names.
4. No EF Core migration is required.
5. Rebuild and run tests before publishing to IIS.

RECOMMENDED VERIFICATION
------------------------
From the ProjectManagement project root:

  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
  dotnet build .\ProjectManagement.csproj

The application build also runs the committed Notebook frontend build target.
If your local node_modules folder is not present, restore the project's normal
Node dependencies first using the repository's established npm workflow.

MANUAL SMOKE TEST — SCREENSHOT 1
--------------------------------
Given:
- BID completed 06 Aug 2026
- TEC ran 07 Aug 2026 to 21 Aug 2026

HoD direct-completes BM with:
- Start 07 Aug 2026
- Complete 21 Aug 2026

Expected:
- Accepted chronologically.
- BM must NOT report TEC completion as its start boundary.
- BM suggestion is based on BID: earliest 06 Aug, suggested 07 Aug.

MANUAL SMOKE TEST — SCREENSHOT 2
--------------------------------
Given:
- BID completed 06 Aug 2026
- TEC has a pending completion on 21 Aug 2026

PO submits BM start 10 Aug 2026.

Expected:
- Submission accepted, assuming all other normal validations pass.
- No error saying BM must wait until TEC completes on 21 Aug.

CONVERGENCE TEST
----------------
If TEC completes 21 Aug and BM completes 25 Aug, COB earliest start = 25 Aug
and suggested start = 26 Aug. Reverse the dates and TEC becomes the controlling
branch.

NOT CHANGED BY THIS PACKAGE
---------------------------
- HoD/PO permissions.
- Direct-completion authority.
- Auto-start policy (only one display-next stage is auto-started as before).
- Database schema.
- Historical dates are not rewritten automatically.
