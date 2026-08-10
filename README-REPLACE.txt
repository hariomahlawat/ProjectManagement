PRISM ERP — SAME-DAY STAGE TRANSITIONS + DURATION SEMANTICS
Ready-to-replace implementation
===========================================================

PURPOSE
-------
This package implements the agreed timeline policy:

1. A successor stage may commence on the SAME calendar day its effective predecessor completes.
2. PRISM continues to SUGGEST the following day as the normal/default start.
3. A start BEFORE predecessor completion remains invalid.
4. Actual stage "Time taken" is inclusive CALENDAR days.
   - Start and finish on the same date = 1 calendar day.
5. Missing historical starts may still be inferred from predecessor completion + 1 day.
6. Planned duration is WORKING days and follows the project's existing weekend/holiday settings.
7. Stage-time analytics now use the same inclusive actual-duration semantics as the project timeline.
8. Bulk actual-date editing enforces the same chronology rule, including coordinated multi-row edits.

INSTALLATION
------------
1. Back up your current project/source tree.
2. Extract this ZIP.
3. Copy the contents of this folder over the ROOT of the ProjectManagement project,
   preserving the included folder structure and replacing existing files when prompted.
4. No database migration is required.
5. Rebuild and run the regression tests in your normal .NET development environment.

IMPORTANT
---------
Do not copy only StageDateSuggestionResolver.cs. The change is deliberately enforced across:
- normal stage validation;
- Project Officer stage requests;
- HoD approval sequence checks;
- HoD/Admin direct stage updates;
- bulk actual-date editing;
- timeline/read models and UI;
- analytics duration calculations;
- exact planned-date editing.

The JavaScript files are loaded directly by the Project Overview page in the supplied source,
so this change does not require regeneration of a project JS bundle.

NO DATABASE MIGRATION
---------------------
No entity/schema change is introduced. This is a chronology-policy, duration-calculation,
UI and regression-test change only.

VERIFICATION COMPLETED IN THIS ENVIRONMENT
------------------------------------------
- node --check wwwroot/js/projects/stages.js                         : PASS
- node --check wwwroot/js/projects/actuals-edit.js                    : PASS
- targeted Node regression suite (changed chronology behaviour)       : 13/13 PASS
- source audit confirms +1 day remains only where intended for
  recommendation/inference, not as the hard chronology boundary.

ENVIRONMENT LIMITATIONS
-----------------------
- The sandbox does not contain the .NET SDK/compiler, so dotnet build/dotnet test could not
  be executed here. This is an environment limitation, not a reported compilation failure.
- The repository-wide npm test run requires jsdom. jsdom was unavailable in the sandbox and
  the sandbox npm mirror returned a package 404 while attempting installation. The targeted
  tests covering the modified JavaScript passed.

RECOMMENDED LOCAL ACCEPTANCE TESTS
----------------------------------
A. Same-day stage transition
   Predecessor completion: 10 Aug 2026
   Successor start:         10 Aug 2026  -> MUST PASS
   Successor start:         09 Aug 2026  -> MUST FAIL
   Suggested successor:     11 Aug 2026  -> MUST REMAIN THE DEFAULT/SUGGESTION

B. Same-day actual duration
   Actual start: 10 Aug 2026
   Completed:    10 Aug 2026
   Timeline:     1 calendar day

C. Inclusive actual duration
   Actual start: 10 Aug 2026
   Completed:    20 Aug 2026
   Timeline/analytics: 11 calendar days

D. Planned working-day duration
   Use an interval spanning a weekend and an office holiday. Exact planned-date save must
   store the number of working days according to ProjectScheduleSettings, not calendar span.

E. Bulk actual editor
   Change predecessor completion and successor start in the same save. The browser and server
   must use the projected predecessor completion. Equality is valid; an earlier successor date is not.

F. Reload
   Reload Project Overview and verify the persisted actual dates, duration and timeline presentation.

FILES
-----
See MANIFEST.txt for the complete replacement set and SHA-256 hashes.
