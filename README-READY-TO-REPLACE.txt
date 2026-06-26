PRISM PROJECT WORKFLOW CONSISTENCY — READY-TO-REPLACE PACKAGE
==============================================================

BASELINE
--------
Prepared against: ProjectManagement-master (95).zip

PURPOSE
-------
This package consolidates project-stage workflow behaviour and the associated
portfolio UI without adding database tables, columns, records or migrations.

IMPLEMENTED
-----------
1. Workflow-version-aware stage policy
   - SDD-1.0: FS -> IPA -> SOW -> AON -> ...
   - SDD-2.0: FS -> SOW -> IPA -> AON -> ...
   - One scoped policy now controls stage order, direct-stage materialisation,
     dependency validation, predecessor closure, cascade completion, auto-start,
     decision warnings and plan ordering.

2. Direct stage updates
   - Any HoD may directly update a project stage.
   - Contextual menu actions are shown by current stage status.
   - Assigned Project Officers retain Request change; HoDs do not see the
     redundant request action, including users who hold both roles.
   - Successful direct changes reload the server-rendered portfolio, eliminating
     temporary stale current-stage, next-stage, plan-warning and backfill states.
   - Authorised completion override uses one consolidated notification.
   - Known actual start is preserved when only the completion date is omitted.

3. Completion and backfill rules
   - Completed-on is authoritative for a completed stage.
   - Actual start is optional for completed stages.
   - Where absent, duration/start can be inferred from the preceding applicable
     completed stage plus one day.
   - Direct completion without a completion date remains available to HoDs and
     creates mandatory backfill.
   - Force completion identifies the full unresolved predecessor chain, not only
     the immediate predecessor.

4. Timeline and planning UI
   - Completed/skipped stages are collapsed in the plan editor by default.
   - Current and future stages remain the primary planning workspace.
   - Current-stage planned completion remains prominent; future dates are optional.
   - Actual-date editor no longer creates horizontal scrolling in the offcanvas.
   - Stage menus are status-aware.

5. Portfolio consistency
   - Header completeness percentage and missing-details panel use one shared list.
   - Project type is included in completeness calculation.

6. Tests and documentation
   - Added workflow-version policy tests, transitive predecessor tests, V2 ordering,
     any-HoD behaviour, inferred start, contextual menu and refresh-contract tests.
   - Updated project and timeline documentation.

INSTALLATION
------------
1. Back up the solution.
2. Copy the package contents into the project root, preserving folders.
3. Delete bin and obj folders from ProjectManagement and ProjectManagement.Tests.
4. Restore packages, clean and rebuild the main project.
5. Rebuild and run ProjectManagement.Tests.
6. Restart IIS Express/application and hard-refresh the browser.

NO DATABASE MIGRATION IS REQUIRED.

RECOMMENDED ACCEPTANCE CHECKS
-----------------------------
- SDD-1.0 project: IPA follows FS and SOW follows IPA.
- SDD-2.0 project: SOW follows FS and IPA follows SOW.
- A HoD not assigned to the project can directly update a stage.
- Completing without a date advances the workflow and creates backfill.
- Existing actual start remains populated after an authorised completion override.
- Completing a later stage with Force predecessors lists/completes the full chain.
- Direct stage update reloads once and displays one consolidated notification.
- HoD stage menu shows only valid direct actions and no Request change.
- Assigned PO stage menu shows Request change and no direct actions.
- Actual dates offcanvas has no horizontal scrollbar.
- Plan editor defaults to current/future stages; historical stages are expandable.
