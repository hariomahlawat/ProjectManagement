PRISM ERP — Action Tasks V2.3 Operational Completeness
======================================================

Purpose
-------
This release completes the individual-task operating model built in V2.2/V2.2.1.
It keeps the proven Peek + Full Task architecture and adds the missing low-friction
management operations directly to the same top action surface.

The design principle remains:
- show every applicable routine action directly;
- do not hide normal work behind menus/accordions;
- open required forms inline under the action bar;
- separate human discussion from immutable workflow history;
- enforce every permission again in the service layer.

What is added
-------------
1. Edit task
   - Comdt / HoD can correct task title and task brief directly from Peek or Full Task.
   - No workflow state change is generated for a metadata correction.
   - The change is concurrency-protected and audit logged.

2. Reassign
   - Comdt / HoD can transfer responsibility directly without returning the task to backlog.
   - Current status and sprint membership are preserved.
   - A reason is mandatory.
   - The responsible person's task role is resolved server-side; no client role value is trusted.
   - A human-visible Progress entry and audit record are written atomically.
   - Previous assignee, new assignee and creator participate in the reassignment notification flow.

3. Change priority
   - Comdt / HoD can change Low / Normal / High / Critical directly.
   - A reason is mandatory.
   - Change is concurrency-protected, appears in the task chronology, is audit logged and uses
     the existing progress-notification path.

4. Task remark governance
   - General remark:
       * author may edit/delete for 3 hours;
       * Comdt / HoD may edit/delete as command override.
   - Conference remark:
       * Comdt / HoD only.
   - Workflow-generated Progress entries:
       * immutable.
   - Deleted remarks are soft-deleted; their audit trail is retained.
   - Attachments belonging to a deleted remark are retired from the active thread but their
     stored file/audit history is not physically destroyed.
   - A former assignee who no longer has task visibility cannot mutate an old remark merely
     because they authored it.

5. Explicit task role policy
   - Comdt / HoD are task planning/command authorities.
   - Assigned user retains owner workflow actions (Start / Block / Resume / Submit as applicable).
   - Admin does not implicitly receive Comdt/HoD task mutation authority.
   - Admin remains recognised by the system-history capability where the task itself is accessible.

6. Overview wording correction
   - “Recent activity” now counts distinct task rows and displays “N task/tasks”.
   - It no longer labels a count of tasks as “N updates”.

7. Shared UX
   - Edit task, Reassign and Change priority are visible in the same top action bar in Peek and
     Full Task.
   - Their forms use the existing inline action tray; no modal or navigation detour is introduced.
   - Remark edit/delete controls are rendered inside the common update timeline used by both views.

No database migration is required.

Production files to replace/add
-------------------------------
Pages/ActionTasks/Details.cshtml
Pages/ActionTasks/Details.cshtml.cs
Pages/ActionTasks/Index.cshtml.cs
Pages/ActionTasks/_TaskDetails.cshtml
Pages/ActionTasks/_TaskActionBar.cshtml
Pages/ActionTasks/TaskActionBarViewModel.cs
Pages/ActionTasks/TaskManagementInputModels.cs              [NEW]
Pages/ActionTasks/TaskUpdateTimelineViewModel.cs
Pages/ActionTasks/_TaskUpdateTimeline.cshtml
Pages/ActionTasks/_TaskDashboard.cshtml
Services/ActionTasks/ActionTaskPermissionService.cs
Services/ActionTasks/ActionTaskInteractionCapabilities.cs
Services/ActionTasks/ActionTaskWorkflowPolicy.cs
Services/ActionTasks/IActionTaskService.cs
Services/ActionTasks/ActionTaskService.cs
Services/ActionTasks/IActionTaskCollaborationService.cs
Services/ActionTasks/ActionTaskCollaborationService.cs
Services/ActionTasks/ActionTaskPresentation.cs
Services/ActionTasks/IActionTaskNotificationService.cs
Services/ActionTasks/ActionTaskNotificationService.cs
wwwroot/js/pages/action-tasks/task-interaction.js
wwwroot/css/action-task-actions.css

Verification-test files included
--------------------------------
ProjectManagement.Tests/ActionTasks/ActionTaskPermissionServiceTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskInteractionCapabilitiesTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskServiceTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskCollaborationServiceTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskNotificationIntegrationTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskPageTests.cs
ProjectManagement.Tests/ActionTasks/ActionTaskProductionReadinessTests.cs
ProjectManagement.Tests/ConferenceTaskCommandServiceTests.cs
ProjectManagement.Tests/ConferenceRemarkCommandServiceTests.cs

Replacement sequence
--------------------
1. Stop the running local application / IIS Express instance.
2. Replace the files above, preserving project-relative paths.
3. Add TaskManagementInputModels.cs if it does not already exist.
4. Clean Solution.
5. Rebuild Solution.
6. Run ProjectManagement.Tests.
7. Start PRISM and use Ctrl+F5.

Recommended smoke test
----------------------
Comdt / HoD:
- Open an Assigned or In Progress task in Peek.
- Edit task title/brief -> save -> verify no status change and updated audit history.
- Reassign -> select another authorised person + reason -> verify assignee changes while status and
  sprint stay unchanged.
- Change priority -> verify new priority, Progress entry and audit history.
- Add General and Conference remarks.
- Edit/delete a General remark.
- Edit/delete a Conference remark as command authority.
- Confirm Progress cards show no edit/delete controls.

Assigned user:
- Add General remark.
- Edit/delete own General remark within 3 hours.
- Verify another user's General remark cannot be changed.
- Verify Conference and Progress entries cannot be changed.

Overview:
- Expand Recent activity and confirm the badge says “N task/tasks”.

Validation completed in this environment
----------------------------------------
- task-interaction.js passes Node syntax validation.
- All 27 C# files in the replacement working set pass lexical brace/parenthesis/bracket balance
  checks after strings/comments are excluded.
- No accidental literal backslash-n insertion remains.
- All known IActionTaskService, IActionTaskCollaborationService and IActionTaskNotificationService
  test doubles in the supplied project have been updated for the expanded interfaces.
- Source-contract tests were updated for direct Edit/Reassign/Priority actions, governed human
  remarks, immutable Progress entries and distinct-task Recent Activity wording.

The .NET SDK is not installed in this execution environment, so the final Roslyn build and xUnit
execution must be completed in Visual Studio after replacement.
