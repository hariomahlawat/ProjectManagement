PRISM ERP — Action Tasks UX V2.4
================================

PURPOSE
-------
V2.4 is the workflow-integrity and command-surface hardening pass on top of the
working V2.3 Task module. It does not redesign the Peek + Full Task model.

CORE CHANGES
------------
1. Awaiting Closure is now a true approval boundary.
   - General/Conference remarks remain available.
   - Command may Accept & close or Return for action.
   - Edit task, Reassign, Priority, Date, Sprint/Backlog replanning and direct
     closure are unavailable while Submitted/Awaiting Closure.
   - Server-side guards enforce the same rule; it is not only a UI restriction.

2. Accept & close and Close directly are now different service commands.
   - A Submitted task cannot be closed through the direct-close override.
   - A non-Submitted task cannot be accepted through the submitted-task command.

3. One shared action implementation is used by Peek and Full Task.
   - _TaskActionBar.cshtml = visible actions.
   - _TaskActionPanels.cshtml = all temporary action forms.
   - Action availability comes from ActionTaskWorkflowPolicy.

4. Reassign uses a searchable local person picker.
   - Search by rank/name/role.
   - Keyboard Arrow Up/Down, Enter and Escape are supported.
   - Actual assignee role is still resolved server-side.

5. Peek Edit Task is deliberately vertical/wider while open.
   - Title and Task Brief are no longer compressed into a narrow horizontal row.

6. Full Task keeps only identity + buttons sticky.
   - The opened action form is normal content immediately below the sticky bar,
     so temporary forms do not consume the viewport while scrolling history.

7. Edited human remarks now show a subtle “edited” indicator.
   - Derived from audit history; no database migration is required.

8. Conference direction governance is operationally visible.
   - Editing a Conference direction notifies the assigned user.
   - Deleting/withdrawing a Conference direction notifies the assigned user.
   - General remark corrections do not generate unnecessary notification noise.

9. Task title/brief changes notify the assigned user.

10. Audit history is stronger.
    - Task-detail audit stores before/after title and full Task Brief snapshots.
    - Reassignment history resolves user IDs to display names.
    - Full Task can expand Task Details changes to view before/after values.

11. Sprint/backlog mutation services also reject Submitted tasks.
    - This closes direct-POST/API bypasses of the Awaiting Closure freeze.

DATABASE
--------
No database migration is required.

INSTALLATION
------------
If V2.3 is currently installed, replace only the files contained in the V2.4
delta package, preserving the folder structure from the project root.

If you want one self-contained superseding Task replacement set, use the full
V2.4 package instead.

After replacement:
  1. Clean Solution
  2. Rebuild Solution
  3. Run ProjectManagement.Tests
  4. Ctrl+F5 / hard refresh the browser

IMPORTANT SMOKE TESTS
---------------------
- In Progress + Comdt/HoD: Edit/Reassign/Priority/Date/Backlog/Direct Close show.
- Submitted + Comdt/HoD: only Accept & close / Return for action + remarks show.
- Attempt a crafted/direct metadata or sprint mutation on Submitted: server rejects.
- Reassign: search and select a responsible person, enter reason, submit.
- Edit a General or Conference remark: “edited” appears in timeline.
- Edit/delete Conference direction: assignee receives notification.
- Edit task title/brief: assignee receives notification and Activity History has
  before/after values.
- Full Task: scroll with an action form open; only identity/buttons remain sticky.

VALIDATION PERFORMED IN GENERATION ENVIRONMENT
----------------------------------------------
- JavaScript syntax validation with Node: passed.
- C# lexical/brace structural validation: passed for all supplied C# files.
- IActionTaskService and IActionTaskNotificationService implementation contract
  checks: passed for production services and included test doubles.
- Focused V2.4 source-contract checks: passed.

The generation environment does not contain the .NET SDK, so Roslyn compilation
and the xUnit suite must be run in Visual Studio after replacement.
