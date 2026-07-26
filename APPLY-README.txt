PRISM PROJECT PORTFOLIO — TERMINAL REMARKS AND LEGACY HISTORY
=============================================================

PACKAGE PURPOSE
---------------
This replacement set completes the project-portfolio refinement:

1. Active projects open the right-hand workspace on Timeline.
2. Completed and cancelled projects open it on Remarks.
3. An explicit URL panel/stage deep link overrides the default.
4. A user's in-session panel choice is retained per project and lifecycle.
5. Admin and HoD users can add or correct evidence-backed stage history for
   legacy completed/cancelled projects.
6. Historical entries use the existing ProjectStages and StageChangeLogs
   tables; the project lifecycle is never reopened.
7. Cover-photo failure handling has one owner and reliably reveals the fallback.
8. Long command-header titles wrap on balanced word boundaries.

REPLACEMENT
-----------
Copy the package contents into the ProjectManagement application root.
Allow folder merge and replace matching files while preserving the relative
paths. REPLACEMENT-MANIFEST.txt is the authoritative file list.

No database migration is required by this package.

HISTORICAL STAGE ENTRY
----------------------
For an eligible project:

  Overview > Timeline > Timeline actions > Manage historical stage data

When no stage history exists, the Timeline empty state also shows
"Add historical stage data".

Access is restricted to Admin and HoD. The project must be:

  - marked as a legacy record; and
  - completed or cancelled; and
  - not in the recycle bin.

Supported outcomes:

  - Completed (actual start and completion dates may be recorded if known)
  - Skipped
  - Ceased at cancellation (cancelled projects only)
  - Not recorded (non-destructive; it never erases existing history)

Every accepted change requires an evidence/source note, writes the standard
ProjectStage row, adds an evidence-bearing StageChangeLog entry, and writes the
Projects.HistoricalStageHistoryUpdated audit event. Those writes share one
transaction, so stage history is not committed if the formal audit cannot be
stored.

Historical dates cannot be in the future or later than the recorded lifecycle
boundary. Exact completion dates are honoured exactly; month-only and year-only
legacy completion values are honoured through the end of that recorded period.

BUILD AND VERIFY
----------------
From the application root:

  npm ci
  npm test
  dotnet restore
  dotnet build -c Release
  dotnet test -c Release

Publish and deploy through the application's normal production process. Use a
new publish output and preserve production configuration and connection strings.

POST-DEPLOYMENT CHECKS
----------------------
1. Active overview: Timeline is initially selected.
2. Completed overview: Remarks is initially selected; Timeline remains usable.
3. Cancelled overview: Remarks is initially selected; #timeline opens Timeline.
4. Admin/HoD legacy terminal project: historical editor is available.
5. Non-Admin/HoD or active/non-legacy project: historical editor is unavailable.
6. Save one verified historical stage and confirm it appears on Timeline while
   the project's lifecycle status remains unchanged.
7. Force a missing cover image and confirm the landscape fallback appears.
