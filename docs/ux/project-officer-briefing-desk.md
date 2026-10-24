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

**Card header.** The header tags agree with the checks. When a revised plan is with the HoD for
a missing or overdue PDC, the PDC tag reads "PDC with HoD since …" or "Revised PDC with HoD
since …" in a neutral tone, not "PDC not set" or a red "overdue": the PDC check already counts
that plan as the PO's answer.

**Required and optional.** In "Before you brief", required steps carry the strong button
(`bd-btn--required`), whether the PO completes them on the desk or they go to the HoD; the line
under the button says which. **Improve the record** lists optional record-health gaps; it is
folded to one line ("38% now · up to +53 points · 5 optional items") until opened, and its
buttons are quiet (`bd-btn--optional`).

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

Procurement figures open in a side panel on the desk. The panel is the Project Overview
editor (`Projects/Procurement/_EditFormBody`), built by the shared `ProcurementEditVmFactory`, so
locks and ARPP messages match. It posts to the existing procurement page with a `returnUrl`.
That page returns only to a local `/Workspace` address
(`WorkspaceRouteHelper.SafeDeskReturnUrl`, shared with the plan editor), and otherwise keeps
its original redirect to Project Overview.

Photo and video gaps link to the full gallery and video library pages, not a side panel. The
desk adds its own address as `returnUrl`; the gallery or library passes it to the upload page,
and a successful upload returns to the desk with a toast. Without a local `/Workspace` address
the upload returns to the gallery or library as before.

**Current-stage PDC form.** When a project's current-stage PDC is missing or overdue, and
nothing else would be submitted along with it, "Propose date(s)" opens a short side panel that
asks only for the PDC. "Nothing else" means no private draft, no submission pending or locked,
and the plan open for submission. The panel posts one row to the plan editor's own handler
(`Timeline/EditPlan`, exact mode, Submit): the stage code and name, the new PDC, and the
existing planned start unchanged. The editor writes back exactly the rows it receives, so the
start must travel with the row. The handler then validates and submits the whole plan through
`PlanApprovalService`, exactly as the full editor does. Other stages keep their planned dates.
A successful submission returns to the desk (`ReturnUrl`, local `/Workspace` only). A refusal
keeps the editor's existing behaviour: the full editor reopens on Project Overview with the
entry and the reasons. In every other state the check links to the full editor.

**Returned items are not on the desk** (changed 2026-10-24). When the HoD returns a plan,
actual-date correction, stage change, project details or document request, that decision is
finished: the PO learns of it from the notification and from the HoD's note at the top of the
editor. The desk lists only what is still pending, so a returned item neither appears on the
card nor stops it being brief-ready, and there is no Returned to you panel. Before this change
a return stayed listed until a newer submission was pending or approved, so it stuck when an
Admin or HoD saved the plan directly, the PO discarded the correction draft or withdrew the
resubmission, or the project was completed or handed to another PO.
`tools/dev-data/returned-plans-demo-seed.sql` recreates those cases in a development database.

Approval-routed work (plan dates, actual-date corrections, project details, documents) is
linked to the exact editor. Plan links use `?oc=plan&stage=<code>`, which opens the plan
editor with that stage's planned due date focused. While a submission is pending, the card
shows **With HoD** and the item appears under **With others**.

## Weekly round

**Start weekly round** walks the PO through every not-ready card, one at a time, in desk order.
The meters, the side column, the section headings and Recently completed are hidden while it
runs. The round is
kept in `sessionStorage` (this tab only), so a post that reloads the page resumes on the same
card. It ends with a summary of how many items from the round are now brief-ready. If storage
is unavailable, the round still works within the page.

## Recently completed projects

Completed projects stay in the Conference view for a while, so the PO may still be asked about
them. They appear in a collapsed **Recently completed** section under the desk, built from the
Conference view's own items (`IsRecentlyCompleted`). It opens by itself when a direction there
still needs an answer. These projects never count towards brief-ready, have no weekly-update
or timeline checks, and are not part of the weekly round.

## Workspace menu

The PO menu has five entries: **Briefing desk**, **My conference review**, **Project register**,
**My documents** and **My ERP activity**.

- **Project register** (`view=projects`) is a table built from the desk's own project cards
  (stage and PDC, last update, record health with the gap breakdown, and readiness). The two
  pages cannot disagree. Each row links to its desk card.
- The Action queue, Assigned tasks, My ideas and Follow-ups views were folded into the desk.
  Their addresses still work and redirect to the desk (`IsFoldedIntoDesk`): tasks to
  `#bd-section-task`, ideas to `#bd-section-idea`, follow-ups to `#bd-week-title`.
- The task and idea registers are linked from the desk's Tasks and Ideas section headings. The
  side panel lists reminders and events for the **next 14 days**.

The previous overview's Command view card and ERP activity heatmap were removed from the landing
page. ERP activity stays under **My ERP activity**.

## Phones

Below 576 px the three meters sit in one compact row and the With HoD count forms one
line under them, so the first card is on the first screen. Unread circulars are counted in the
side column's Reading panel only. The workspace rail, folded, becomes a row of its icons above
the page (`command-workspace.css`, all workspace pages); expanding it opens the usual overlay.
