PRISM ERP — Action Tasks UX V2.1 (Low-Friction Operational Workspace)
====================================================================

PURPOSE
-------
This phase keeps the V2 Peek + Full Task architecture and removes the remaining
interaction friction. Valid actions are now role-aware and state-aware: normal
operations stay visible, irrelevant operations disappear, and consequential
commands expand inline only when the user selects them.

KEY UX CHANGES
--------------
1. Task Peek
   - Full-task icon is always visible in the header.
   - "Open full task" is also kept in a persistent footer; no scrolling is
     required to discover the full workspace.
   - Owner workflow actions remain direct: Start work, Resume, Submit for
     closure and Block.
   - Command users no longer see an orphaned Block button as the apparent
     primary action for somebody else's task.
   - Legitimate command controls remain visible in a separate Task controls
     strip: Block task, Resume task, change due/target date, Close directly and
     Planning controls where applicable.
   - Change date is available inline in the Peek.
   - Remark composer stays permanently visible.
   - Latest three updates use the same shared timeline component as the full
     workspace.

2. Full Task Workspace
   - Header is sticky and compacts while scrolling so task identity and current
     primary actions are always available.
   - Generic "Manage task" accordion has been removed.
   - Task details remain visible at all times.
   - Due/target date exposes a visible Change action and expands inline.
   - Task controls are always visible when authorised/relevant; sprint/backlog
     operations expand directly below the selected control.
   - Direct command closure is explicitly labelled "Close directly" to
     distinguish it from the normal Submit -> Accept & close workflow.
   - Activity history remains discoverable but collapsed; old/new values are
     rendered in plain language.

3. Timeline
   - Peek and full workspace now render through one shared partial.
   - General, Conference and Progress remain one chronological stream.
   - Workflow-generated Progress entries receive semantic presentation such as
     Work started, Work resumed, Task blocked and Submitted for closure rather
     than exposing raw persistence terminology where possible.
   - Attachments remain visually attached to the originating update.

4. Architecture
   - ActionTaskInteractionCapabilities centralises role/state action visibility.
   - Both Peek and full workspace consume the same capability rules.
   - ActionTaskPresentation centralises human-facing timeline/audit labels.
   - No database migration is required.

REPLACE / ADD THESE FILES
-------------------------
Pages/ActionTasks/Details.cshtml
Pages/ActionTasks/Details.cshtml.cs
Pages/ActionTasks/Index.cshtml
Pages/ActionTasks/Index.cshtml.cs
Pages/ActionTasks/_TaskDetails.cshtml
Pages/ActionTasks/_TaskMyWorkQueueRows.cshtml
Pages/ActionTasks/_TaskUpdateTimeline.cshtml                         [NEW]
Pages/ActionTasks/TaskUpdateTimelineViewModel.cs                    [NEW]

Services/ActionTasks/ActionTaskCollaborationService.cs
Services/ActionTasks/ActionTaskInspectorReadModelBuilder.cs
Services/ActionTasks/ActionTaskInteractionCapabilities.cs           [NEW]
Services/ActionTasks/ActionTaskNotificationService.cs
Services/ActionTasks/ActionTaskPermissionService.cs
Services/ActionTasks/ActionTaskPresentation.cs                      [NEW]
Services/ActionTasks/ActionTaskService.cs
Services/ActionTasks/ActionTaskWorkflowPolicy.cs
Services/ActionTasks/IActionTaskService.cs

wwwroot/css/action-task-peek.css
wwwroot/css/action-task-workspace.css
wwwroot/js/pages/action-tasks/task-interaction.js

Tests included in the package should also replace/add the corresponding files.

IMPORTANT
---------
- This package includes the previous UpdateSprintInput.RowVersion hotfix.
- No EF Core migration is required.
- After replacement: Clean -> Rebuild -> run ProjectManagement.Tests -> Ctrl+F5.
- The full .NET build could not be executed in the generation environment
  because the .NET SDK is not installed there. JavaScript syntax validation was
  run with Node.
