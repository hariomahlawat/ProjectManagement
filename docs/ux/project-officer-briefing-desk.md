# Project Officer briefing desk

The Project Officer landing page (`/Workspace?mode=project-officer`, view `overview`) is a
**briefing desk**. A PO prepares here and briefs from **My conference review**. Each card
answers one question: if the conference were today, would this project, idea or task be ready
to brief?

## Where it lives

| Concern | Code |
| --- | --- |
| Brief-ready rules (pure, unit-tested) | `Services/Workspace/BriefingDeskBuilder.cs` |
| Data loading (projects, tasks, ideas, pending approvals, conference view) | `ProjectOfficerWorkspaceService.PopulateOverviewAsync` |
| Inline posts from the desk | `Services/Workspace/BriefingDeskCommandService.cs`, `IndexModel.OnPostDesk*` handlers |
| View models | `ViewModels/Workspace/BriefingDeskViewModels.cs` |
| Views | `Pages/Workspace/_ProjectOfficerBriefingDesk.cshtml`, `_BriefingDeskItem.cshtml`, `_BriefingDeskCheck.cshtml` |
| Styles and behaviour | `wwwroot/css/workspace-briefing-desk.css` (site tokens only), `wwwroot/js/pages/workspace-briefing-desk.js` |

The **See as the Comdt sees it** preview shows the item from
`IOfficerConferenceReadService.GetForProjectOfficerAsync`, the same read model as the Conference
view, so the preview cannot disagree with what is shown in conference.

## Brief-ready rules

Sections follow the Conference view order: Projects, Ideas, Tasks. Empty sections are hidden.

| Item | Brief-ready when | Shown but never required |
| --- | --- | --- |
| Project | A PO update (any remark by the PO) within 7 days; the latest conference direction answered; a current-stage PDC that is set and not overdue, or a plan awaiting HoD approval; an in-progress stage has an actual start, or a date correction awaiting approval | Record-health gaps, grouped by the editor that fixes them, with the points each would add |
| Idea | Activity within the existing 15-day idea rule; the latest direction answered with a note | — |
| Task | Progress by the PO within 7 days; the latest direction answered; if overdue, submitted for closure or flagged blocked. A submitted task waits on the assigner and counts as ready | — |

A direction counts as answered under the same rule `ProjectOfficerConferenceActionQuery` uses:
any later remark, idea note or task update by the PO.

## What the PO can finish on the desk

All posts go through the existing services, so audit records, notifications and workflow rules
are unchanged. The desk adds its own check that the item is assigned to the signed-in PO.

| Action | Service used | Result |
| --- | --- | --- |
| Project update | `IRemarkService.CreateRemarkAsync` (Internal remark, Project Officer actor) | Counts as the weekly update and as progress on a direction |
| Idea note | `ProjectIdeaCommandService.AddNoteAsync` | Note titled "Progress update · date" |
| Task progress | `IActionTaskCollaborationService.AddUpdateAsync` (Comment) | Counts as task progress |
| Submit task for closure | `IActionTaskService.SubmitTaskAsync` | Waits on the assigner |
| Flag task blocked | `IActionTaskService.UpdateStatusAsync` (Blocked, reason required) | Blocker shown in conference |

Approval-routed work (plan dates, actual-date corrections, project details, documents) is
linked to the exact editor. Plan links use `?oc=plan&stage=<code>`, which opens the plan
editor with that stage's planned due date focused. While a submission is pending, the card
shows **With HoD** and the item appears under **With others**.

## Not on the desk

The previous overview's Command view card and ERP activity heatmap were removed from the
landing page. ERP activity stays under **My ERP activity**. The Action queue, Assigned
projects and other PO views are unchanged.
