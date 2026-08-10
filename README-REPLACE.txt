PRISM ERP — Action Tasks UX V2.2
Unified Command Surface & Density
=================================

PURPOSE
-------
V2.2 keeps the proven Peek + Full Task architecture but removes the remaining
spatial friction. The user should never have to click a control at one location
and then be moved elsewhere on the page to complete it.

CORE UX RULE
------------
Every action that is currently valid for the user's role and the task's state is
visible at the top. Selecting an action opens its required fields immediately
under the same action bar. No Manage accordion, no generic Planning controls,
no remote forms, and no automatic page jumping.

WHAT CHANGED
------------
1. One shared task action bar
   - New shared partial: Pages/ActionTasks/_TaskActionBar.cshtml
   - New shared model:   Pages/ActionTasks/TaskActionBarViewModel.cs
   - Used by both Task Peek and Full Task Workspace.
   - Same ordering, labels and role/state visibility in both surfaces.

2. All applicable controls are at the top
   - Start work / Resume
   - Submit for closure
   - Block task
   - Accept & close / Return for action
   - Change due/target date
   - Assign/Add/Remove sprint when applicable
   - Return to backlog when applicable
   - Close directly when the normal closure path is not available
   - Generic "Planning controls" link removed.

3. One action tray directly under the action bar
   - Required remarks, dates and sprint selectors open in one fixed location.
   - No scroll-to-form behaviour.
   - Escape or Cancel closes the active tray.
   - Validation failures reopen the exact action that failed.

4. Close semantics are explicit
   - "Accept & close" and "Close directly" are separate UI intents.
   - Submitted tasks use the normal acceptance path.
   - Direct closure remains an exceptional command override.

5. Full Task page is substantially denser
   - Removed the large hero treatment.
   - Back, task number and title now share one compact identity line.
   - Metadata is directly below it.
   - The compact operational header is sticky from the start; it does not
     transform into a second visual mode while scrolling.
   - Task Brief padding is reduced so Discussion appears earlier.

6. Right rail is information-only
   - Task Details remains visible.
   - Activity History remains visible/collapsed.
   - Due-date edit and the Task Controls card are removed from the rail because
     those operations now live in the top action surface.

7. Task Peek is wider and more useful
   - Desktop width increased from 32rem to a maximum of 40rem.
   - At medium widths it uses a 36rem cap; mobile remains full width.
   - The action bar and its action tray are outside the Peek's scrolling body,
     so task operations remain visible while reading updates.
   - Composer is slightly larger and recent updates retain the shared timeline.

8. JavaScript no longer moves the page for task commands
   - task-interaction.js opens/focuses the local action tray only.
   - No automatic scroll-to-command-form behaviour.
   - Existing remark composer validation and Ctrl/Cmd+Enter remain intact.

FILES ADDED IN V2.2
-------------------
Pages/ActionTasks/_TaskActionBar.cshtml
Pages/ActionTasks/TaskActionBarViewModel.cs
wwwroot/css/action-task-actions.css

KEY FILES UPDATED IN V2.2
-------------------------
Pages/ActionTasks/Index.cshtml
Pages/ActionTasks/Index.cshtml.cs
Pages/ActionTasks/_TaskDetails.cshtml
Pages/ActionTasks/Details.cshtml
Pages/ActionTasks/Details.cshtml.cs
wwwroot/css/action-task-peek.css
wwwroot/css/action-task-workspace.css
wwwroot/js/pages/action-tasks/task-interaction.js
ProjectManagement.Tests/ActionTasks/ActionTaskProductionReadinessTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskPageTests.cs

PACKAGE NOTE
------------
The ZIP is a full superseding Task UX package. It also includes the V2/V2.1
service, timeline, notification and RowVersion changes, so it can safely replace
the previous ActionTasks-UX-V2.1 package as a unit.

DATABASE
--------
No EF Core migration is required.

VALIDATION AFTER REPLACEMENT
----------------------------
1. Clean Solution
2. Rebuild Solution
3. Run ProjectManagement.Tests
4. Ctrl+F5 in browser
5. Verify at least these scenarios:
   - Assigned task as assignee: Start/Submit/Block
   - Blocked task as assignee and command: Resume
   - Submitted task as HoD/Comdt: Accept & close / Return for action
   - In-progress task as HoD/Comdt: Change date / backlog / direct close where allowed
   - Sprint task and outside-sprint task planning actions
   - Peek and Full Task show the same applicable actions
   - Opening any action does not move the page vertically

GENERATION-ENVIRONMENT LIMITATION
---------------------------------
The .NET SDK is not installed in the generation environment, so the actual
solution build/xUnit suite could not be executed here. JavaScript syntax and
focused source-contract checks were run before packaging.
