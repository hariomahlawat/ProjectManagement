PRISM ERP - Action Tasks UX V2
Ready-to-replace package
================================

PURPOSE
-------
This phase reduces task interaction friction by separating collaboration from workflow.
The existing Task Management collection workspaces (Overview / My Tasks / Board / All Tasks)
remain intact. A selected task now uses a lightweight Task Peek for frequent actions, while a
new full Task Workspace handles deep work.

KEY USER EXPERIENCE CHANGES
---------------------------
1. Task Peek is deliberately lightweight:
   - task identity / status / due date
   - task brief
   - state-aware workflow actions
   - always-available remark composer
   - latest 3 updates
   - Open full task

2. Adding a normal remark is now direct:
   - General remark = ActionTaskUpdateTypes.Comment
   - Conference direction = ActionTaskUpdateTypes.Conference
   - Progress is no longer user-selectable in the remark composer
   - no status dropdown is mixed into the remark form
   - General remarks may be text, file-only, or text + file
   - Conference directions require text; supporting files may be attached
   - Comdt defaults to Conference; other users default to General

3. Workflow is command-driven:
   - Assigned -> Start work (one click) / Block
   - In Progress -> Submit for closure / Block
   - Blocked -> Resume (one click)
   - Submitted -> command authority can Accept & close or Return for action
   - Closed -> read-only

4. Return for Action is now an explicit command:
   - HoD / Comdt only under the existing Task command-authority model
   - only from Submitted
   - remarks required
   - returns status to In Progress
   - clears SubmittedOn
   - writes audit + human-visible Progress timeline entry

5. Workflow transitions now write human-visible Progress entries in addition to audit history.
   This makes the Updates timeline a useful operational chronology without exposing a generic
   status dropdown to users.

6. Attachments are rendered inside the update that introduced them.
   Activity history remains a separate audit surface and is omitted from the Peek.

7. New full Task Workspace:
   /ActionTasks/Details/{id}
   - complete update timeline
   - task properties
   - attachments inline with updates
   - collapsed management controls
   - collapsed activity history where authorised
   - preserves a validated local return URL when opened from a collection view

8. My Tasks quick actions are lower-friction:
   - Start work and Resume remain one-click
   - Add remark opens the Peek and focuses the composer immediately
   - Submit for closure opens directly into the required completion-remarks panel
   - full-task links preserve the user's current collection context

9. Notifications now deep-link directly to the full Task Workspace.
   General remarks use task-remark wording; Conference remains direction-specific.

SECURITY / GOVERNANCE HARDENING
-------------------------------
- Conference direction authorisation remains server-side.
- Conference directions require text at service level, not just in the browser.
- Remark body length is enforced at service level (4000 characters).
- Full-workspace backlog assignment resolves the responsible person's role server-side;
  a browser-supplied role is never trusted.
- Full-workspace return links accept only local URLs (Url.IsLocalUrl).
- Task-return permission is enforced in the permission/service layers.
- Generic status mutation cannot be used to bypass the explicit Return for Action command.

COMPATIBILITY
-------------
- No database migration is required.
- Existing ActionTaskUpdate schema already supports Comment / Conference / Progress.
- The legacy AddUpdateAndMaybeChangeStatusAsync service method is retained for compatibility,
  but the V2 UI does not call it.
- Existing collection workspaces, sprint architecture, reports and task visibility rules are
  intentionally not redesigned in this phase.

PRODUCTION FILES TO REPLACE / ADD
---------------------------------
Services/ActionTasks/IActionTaskService.cs
Services/ActionTasks/ActionTaskPermissionService.cs
Services/ActionTasks/ActionTaskWorkflowPolicy.cs
Services/ActionTasks/ActionTaskService.cs
Services/ActionTasks/ActionTaskNotificationService.cs
Services/ActionTasks/ActionTaskInspectorReadModelBuilder.cs
Services/ActionTasks/ActionTaskCollaborationService.cs

Pages/ActionTasks/Index.cshtml
Pages/ActionTasks/Index.cshtml.cs
Pages/ActionTasks/_TaskDetails.cshtml
Pages/ActionTasks/_TaskMyWorkQueueRows.cshtml
Pages/ActionTasks/Details.cshtml                  [NEW]
Pages/ActionTasks/Details.cshtml.cs               [NEW]

wwwroot/css/action-task-peek.css                  [NEW]
wwwroot/css/action-task-workspace.css             [NEW]
wwwroot/js/pages/action-tasks/task-interaction.js [NEW]

TEST FILES INCLUDED
-------------------
ProjectManagement.Tests/ConferenceTaskCommandServiceTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskNotificationServiceTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskServiceTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskProductionReadinessTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskCollaborationServiceTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskPageTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskPermissionServiceTests.cs

REPLACEMENT PROCEDURE
---------------------
1. Back up the existing project / commit the current state.
2. Copy the package contents over the project root, preserving the folder structure.
3. Clean and rebuild the .NET solution.
4. Run ProjectManagement.Tests.
5. Start PRISM and hard-refresh the browser (Ctrl+F5) so the new CSS/JS is used.
6. Smoke-test at least:
   - Assigned assignee: Start work, Add remark
   - In Progress assignee: Add remark, attachment, Submit for closure
   - Blocked assignee: Resume
   - HoD: General remark, Conference direction, Return for action, Accept & close
   - Comdt: Conference default, General toggle, Return for action, Accept & close
   - full task workspace from My Tasks and from notification/deep link
   - current collection return path after opening a full task

VALIDATION PERFORMED IN THIS ENVIRONMENT
----------------------------------------
- 40/40 Task UX V2 static source contracts passed.
- task-interaction.js syntax check passed with Node.
- existing Action Tasks index.js syntax check passed with Node.
- changed ActionTasks Razor files contain no new inline scripts, inline event handlers or inline styles.
- repository-wide view guardrail script was also run; it reports pre-existing inline-style
  violations in unrelated modules, so it cannot currently return green for the repository as a whole.

IMPORTANT
---------
The .NET SDK is not installed in this execution environment, therefore a real C# / Razor compile
and xUnit run could not be performed here. Run Clean/Rebuild and ProjectManagement.Tests on the
normal development machine before production deployment.
