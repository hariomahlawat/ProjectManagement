PRISM BRIEFING DECK — ADDITIONAL SLIDES WORKSPACE (CUMULATIVE FIX)
==================================================================

Purpose
-------
This cumulative package implements the modular Additional Slides workspace and includes
the Razor namespace correction required by the new SDD Institutional Profile editor.
Use this package instead of applying the earlier workspace package and namespace fix
separately.

Implemented workflow
--------------------
1. Deck Settings contains only a compact include/exclude control for the SDD profile.
2. A new Additional Slides section appears between Deck Preflight and Projects.
3. The SDD Institutional Profile is represented as an independent slide card with:
   - enabled/not-included state;
   - fixed position summary (after cover);
   - milestone/module summary;
   - immediate include/remove switch;
   - dedicated Edit action.
4. Detailed profile configuration opens in its own focused drawer.
5. The profile has an isolated save handler, validation and dirty-state protection.
6. Deck-wide settings preserve the complete profile configuration and only change its
   include/exclude state.
7. Profile edits and enable/disable actions use optimistic concurrency and audit logging.
8. Server-side validation failures reopen the profile editor automatically.
9. The partial imports ProjectManagement.Services.ProjectBriefings, resolving:
   CS0103: ProjectBriefingInstitutionalProfileOptions does not exist in the current context.
10. No database migration is required; the existing versioned deck JSON remains the
    authoritative configuration store.

Ready-to-replace files
----------------------
Copy every production file in this package over the matching project-relative path.
The new partial must also be added:
  Pages/Workspace/BriefingDecks/_InstitutionalProfileEditor.cshtml

The two ProjectManagement.Tests files are regression-test replacements and should be
copied into the test project when that project is present in the solution.

After replacement
-----------------
1. Close the running application/IIS Express instance.
2. Clean the solution.
3. Rebuild the solution.
4. Run ProjectManagement.Tests, especially the ProjectBriefings tests.
5. Start the application and hard-refresh the browser with Ctrl+F5.

Validation performed in this environment
----------------------------------------
- Cumulative replacement package assembled and namespace correction verified.
- JavaScript syntax validation passed.
- Project briefing JavaScript tests passed: 27/27.
- Test project XML validation passed.
- Cumulative patch dry-run and clean-application comparison passed.
- ZIP integrity and SHA-256 manifest validation passed.

The .NET SDK is not installed in this environment, so the final .NET compile/test run
must be performed in Visual Studio after replacement.
