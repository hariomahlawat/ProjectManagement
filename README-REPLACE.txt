PRISM ERP — Action Tasks UX V2.4.1 UI Refinement
=================================================

BASELINE
--------
Apply this package on top of the working Task V2.4 implementation.
This is deliberately a presentation/interaction refinement only. It does not
change workflow rules, permissions, handlers, services, database schema, or
notification semantics.

REPLACE THESE FILES
-------------------
1. Pages/ActionTasks/_TaskActionBar.cshtml
2. Pages/ActionTasks/_TaskActionPanels.cshtml
3. wwwroot/css/action-task-actions.css
4. wwwroot/js/pages/action-tasks/task-interaction.js

WHAT CHANGED
------------
- Action bar is intentionally grouped into:
    workflow | task management | command override
  while retaining one-click visibility of every valid action.
- Compact management labels are used where the action is obvious:
    Priority, Due date/Target date, To backlog.
  Tooltips/ARIA retain the full action meaning.
- Edit Task now has explicit Title and Task brief labels.
- Reassign picker:
    * wider result surface;
    * single-line names where possible;
    * compact role badges;
    * keyboard Arrow Up/Down + Enter selection;
    * Escape closes picker results before closing the action tray;
    * aria-activedescendant / aria-selected support;
    * viewport-aware drop-up and right-edge alignment.
- Priority is a four-choice one-click segmented control instead of a select.
- Peek Edit Task remains intentionally vertical for comfortable content editing.

NO MIGRATION REQUIRED
---------------------
No EF Core migration or database change is required.

SMOKE TEST
----------
After Clean/Rebuild:
1. Open an In Progress task as Comdt/HoD in Peek.
2. Confirm toolbar grouping and that all valid actions remain one click away.
3. Edit task: confirm labels, title and multiline brief are comfortable.
4. Reassign:
   - search by name;
   - search by role;
   - use Arrow Down + Enter;
   - press Escape while results are open (only results should close);
   - verify list stays inside viewport near the right edge.
5. Priority: select Low / Normal / High / Critical and save with reason.
6. Due date / To backlog / Close directly: verify existing V2.4 behaviour is unchanged.
7. Repeat on Full Task to confirm the same shared components behave consistently.

VALIDATION PERFORMED HERE
-------------------------
- JavaScript passes Node syntax validation.
- CSS brace structure checked.
- Razor source contract checks for action grouping, field labels, person-picker
  accessibility hooks, and segmented priority passed.

Because the .NET SDK is not available in this execution environment, run the
normal Visual Studio Clean Solution -> Rebuild Solution -> ProjectManagement.Tests
sequence after replacing the files.
