PRISM ERP — Action Tasks UX V2.4.2 Sticky / Command-Form Polish
================================================================

BASELINE
--------
Apply this package on top of the working V2.4.1 UI refinement (including the
RZ1010 Razor fix already applied to _TaskActionPanels.cshtml).

This is deliberately a presentation/interaction correction only. It does NOT
change workflow rules, permissions, services, handlers, database schema,
notifications, task state semantics, or Razor action-panel markup.

REPLACE THESE FILES
-------------------
1. wwwroot/css/action-task-actions.css
2. wwwroot/css/action-task-peek.css
3. wwwroot/css/action-task-workspace.css
4. wwwroot/js/pages/action-tasks/task-interaction.js

WHAT V2.4.2 FIXES
-----------------
1. Full Task sticky-header geometry
   - Removes the visible gap between the global PRISM navigation and the sticky
     Task header.
   - The offset is derived from the actual rendered sticky application chrome:
       .pm-topbar
       .pm-module-subnav-wrap
   - Desktop, mobile and pages with module sub-navigation are therefore handled
     from the rendered geometry rather than a stale hard-coded 64px offset.
   - The Task header now stays below the application shell in the z-index stack.
   - The Task header background is opaque so scrolled discussion content cannot
     bleed through it.

2. Remark edit/delete control regression
   - Restores the governed human-remark action styles accidentally dropped when
     action-task-actions.css was replaced in V2.4.1.
   - Pencil / trash controls return to quiet inline icon buttons instead of raw
     browser button chrome.
   - Restores edit-panel and "edited" provenance styling.
   - Adds an explicit keyboard focus treatment.

3. Peek Reassign composition
   - Keeps the existing searchable assignee picker and keyboard behaviour.
   - On the 40rem desktop Peek, the search control receives the full command
     width on row 1.
   - Reason + Reassign/Cancel use row 2.
   - Medium/mobile behaviour remains stacked and unchanged.

4. Peek Priority composition
   - Keeps the four-choice segmented priority control.
   - Gives the segmented control the full command width on row 1.
   - Reason + Save/Cancel use row 2, preventing the narrow scrolling textarea
     seen in V2.4.1.
   - Full Task priority layout remains unchanged.

NO RAZOR / DATABASE CHANGE
--------------------------
No .cshtml, C#, EF Core migration, service contract, or database change is
required in this package.

REPLACEMENT PROCEDURE
---------------------
1. Commit / back up the current working V2.4.1 state.
2. Stop the running local application / IIS Express instance.
3. Copy the four files above over the project root, preserving folder paths.
4. Clean Solution.
5. Rebuild Solution.
6. Run ProjectManagement.Tests.
7. Start PRISM and hard-refresh the browser (Ctrl+F5).

SMOKE TEST
----------
Full Task:
1. Open a task and scroll deep into the update history.
2. Confirm global PRISM navigation remains on top.
3. Confirm Task identity/status/action bar touches the bottom of the visible
   application navigation with no content strip between them.
4. Confirm scrolled remarks do not show through the Task header.
5. Edit/delete a General remark and a Conference remark; confirm pencil/trash
   controls are quiet inline icons and retain their permission behaviour.

Peek:
6. Open Reassign on a desktop-width Peek.
   - Search field should show "Search rank or name…" comfortably.
   - Results list remains viewport-safe.
   - Reason field has usable width.
   - Reassign / Cancel are aligned on the second row.
7. Open Priority.
   - Low / Normal / High / Critical remain visible in one segmented control.
   - Reason field is no longer a narrow scrolling column.
8. Verify Due date, To backlog, Edit task and Close directly remain unchanged.
9. Repeat at a medium browser width and mobile/responsive width; Reassign and
   Priority should fall back to the existing stacked command layout.

VALIDATION PERFORMED HERE
-------------------------
- task-interaction.js passes `node --check`.
- All three CSS files pass structural brace-balance validation.
- V2.4.2 changes are isolated to the four files listed above.
- No Razor file is touched, so the prior RZ1010 correction is preserved.

ENVIRONMENT LIMITATION
----------------------
The .NET SDK is not installed in this execution environment. Run the normal
Visual Studio Clean -> Rebuild -> ProjectManagement.Tests sequence on the
actual development machine before deployment.
