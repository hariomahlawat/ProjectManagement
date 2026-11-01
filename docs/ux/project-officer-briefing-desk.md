# Project Officer briefing desk

The Project Officer landing page (`/Workspace?mode=project-officer`, view `overview`) is a
**briefing desk**. A PO prepares here and briefs from **My conference review**. Each card
answers one question: if the conference were today, would this project, idea or task be ready
to brief?

## Where it lives

| Concern | Code |
| --- | --- |
| Ready-for-conference rules (pure, unit-tested) | `Services/Workspace/BriefingDeskBuilder.cs` |
| Data loading (projects, tasks, ideas, pending approvals, conference view) | `ProjectOfficerWorkspaceService.PopulateOverviewAsync` |
| Inline posts from the desk | `Services/Workspace/BriefingDeskCommandService.cs`, `IndexModel.OnPostDesk*` handlers |
| View models | `ViewModels/Workspace/BriefingDeskViewModels.cs` |
| Views | `Pages/Workspace/_ProjectOfficerBriefingDesk.cshtml`, `_BriefingDeskItem.cshtml`, `_BriefingDeskCheck.cshtml` |
| Styles and behaviour | `wwwroot/css/workspace-briefing-desk.css` (site tokens only), `wwwroot/js/pages/workspace-briefing-desk.js` |

The **See as the Comdt sees it** preview shows the item from
`IOfficerConferenceReadService.GetForProjectOfficerAsync`, the same read model as the Conference
view, so the preview cannot disagree with what is shown in conference.

## Ready-for-conference rules

Sections follow the Conference view order: Projects, Ideas, Tasks. Empty sections are hidden.

| Item | Ready for conference when | Shown but never required |
| --- | --- | --- |
| Project | A PO remark within 10 days (amber from 7 days, red and not ready from 10); the current stage confirmed on a PO remark within the same 10 days; the latest conference direction answered; a current-stage PDC that is set, or a plan with the HoD; no procurement figure missing for the stages already completed | A PDC that has passed (a plain date, never an alarm); completion dates of earlier stages and the other record-health gaps, listed under **Improve the record** |
| Idea | The latest conference direction answered with a note. Otherwise a note is optional and there is no time limit | — |
| Task | Progress by the PO within 7 days; the latest direction answered; if overdue, submitted for closure or flagged blocked. A submitted task waits on the assigner and counts as ready | — |

A direction counts as answered under the same rule `ProjectOfficerConferenceActionQuery` uses:
any later remark, idea note or task update by the PO.

**Submitted is done** (changed 2026-10-29). A missing PDC answered by a plan with the HoD, missing
completion dates answered by a date correction with the HoD, and a task submitted for closure
are the PO's part done. The step shows a green tick and "Sent to HoD on …" or "Sent to assigner
on …", counts towards ready for conference, and asks nothing more. It comes back as a step if the HoD
returns it and the gap is still open. Plans and date corrections are project-scoped, so one
sent by someone else (for example a previous PO) counts too.

A date correction counts only for the stages whose completion date it proposes; any other
missing completion date stays a step ("Your date correction with the HoD does not include …").
If official dates changed after the correction was sent, the HoD cannot approve it until the PO
reviews and resubmits it, so the step reads "Review your date correction" instead of done (even
when no completion date is missing, for example a proposed change to a recorded date), and the
correction is not listed under **Sent for approval**.

**Project steps** (changed 2026-11-01). A project card lists its steps in the order the PO
works through them:

1. **Remark.** Green under 7 days, amber ("Due soon", the box open) from 7, red from 10, when the
   project stops being ready for conference (`UpdateWindowDays`, `UpdateDueSoonDays`).
2. **Stage.** The remark box carries a tick, on by default: "Stage is still DEVP · Development".
   Ticked, the remark is saved with the stage (`Remark.StageRef`, `StageNameSnapshot`); that is
   the PO's confirmation. The server checks the posted code against the project's current stage
   and refuses the post if the stage has moved since the page loaded. Any PO remark tagged with
   the current stage within 10 days counts, wherever it was posted
   (`ProjectOfficerWorkspaceService.LoadStageConfirmationsAsync`). Unticked, the remark posts
   normally and the stage step stays open. "Wrong stage? Change it" opens the project page in
   the project's own tab (`ProjectTab.Name`), where the stage is changed.
3. **PDC.** A missing current-stage PDC is a step. Its side panel first asks "Is DEVP ·
   Development the project's current stage?": **Yes** shows the PDC form (submitted to the HoD,
   as before); **No** opens the project page in the project's tab. A PDC that has passed is a
   plain date and a done step, never a red note.
4. **Procurement.** Figures that the completed stages make applicable (record health decides
   which: an AoN cost once AoN is done, and so on) are a required step, edited in the procurement
   side panel on the desk.

**Gaps that never block** (changed 2026-11-01). Completion dates of earlier stages (the
`<STAGE>_ACTUAL_COMPLETION` gaps) are listed first under **Improve the record**, with their date
correction state ("Sent to HoD on …", "Review your date correction"), followed by the other
record-health gaps. When a published ARPP record supplies the IPA completion date
(`IArppIpaStageAuthorityService`), the date editor locks it, so a missing stored IPA date is
never listed. Actual start dates are optional and are not asked for on the desk.

**Rollout.** Remarks written before this change carry no stage, so on the first day every
project shows its stage step open until its PO posts a remark with the tick. This was accepted
as a one-time confirmation of every project's stage.

**Wording.** On screen, "brief-ready" is **Ready for conference** (pill: "Ready"). The code
keeps the name `IsBriefReady`.

**Lengths.** The update box takes up to 4000 characters. A closure note or a blocker takes up to
2000, because `ActionTaskService` also writes it to the task's audit log (2000-character
`Remarks`). A project remark is checked again after `RemarkService` encodes `&`, `<` and `>`, so
one that would overflow the stored 4000 characters is refused with a reason rather than failing
the save. "Flag blocked" on a task that is already blocked is refused ("Post the new reason as
progress instead"), because the task service would save nothing.

**Layout** (changed 2026-10-29).
- The headline leads with what needs the PO ("3 items need you before the next conference");
  the line under it gives the ready count. A recently completed project with a direction to
  answer counts as needing the PO here (and the line says so), though it never counts towards
  ready for conference; the empty state shows only when there is nothing at all.
- In each section, items that need the PO are full cards, first one open. Ready items sit in a
  folded group under them, opened from a quiet "N ready for conference" line (not a bar), one
  line each. The group opens by itself when a post,
  `focus` or a URL hash points at one of its cards, and the PO can still open a ready card to
  add a remark.
- The per-section meters appear only when the desk has more than one section.
- A folded card shows its tags, its pill and the next step. An open card shows the stage strip
  (the current stage with its name) and its steps, and hides the tags its steps repeat
  (`CoveredByStep`: the update tag, and the PDC tag when there is a PDC step) and the next-step
  line. A passed PDC has no step, so its red note stays.
- The stage tag names the stage from the workflow definition ("TEC · Technical Evaluation ·
  28 days"), since there is no hover title on a phone. A stage whose name is its code (PNC)
  shows the code once.
- Record health is not a header tag; "Improve the record" shows it with the gaps.
- An overdue task's due tag is covered by its "Overdue by" step; an idea shows its document
  count only when it has documents. The section heading keeps the count beside the title and
  any register link on the right.
- Side panels with nothing to show (no unread circulars, nothing in the next 14 days) are one
  line each, and **Sent for approval** is left out when nothing is pending.

There is no weekly round (removed 2026-10-29): the "needs you" cards, in order, are the round.

**Card header.** The header tags agree with the checks. When a plan is with the HoD for a
missing or passed PDC, the PDC tag reads "PDC sent to HoD on …" or "Revised PDC sent to HoD on
…" in a neutral tone, not "PDC not set". A passed PDC is shown as its date ("PDC 07 Oct 2026").
Inside a section the "Project" / "Idea" / "Task" kind label is kept for screen readers only,
since the section heading says it.

**Update box.** When a remark is due (amber or red) or a direction waits, the box is open on the card. When
nothing is due, the card says "Not needed now" with the date of the last update, and the box
folds behind "Add another remark" (or note, or progress), so the PO can still post.

**Required and optional.** In "Before you brief", required steps carry the strong button
(`bd-btn--required`), whether the PO completes them on the desk or they go to the HoD; the line
under the button says which. **Improve the record** lists the optional record-health gaps,
missing stage completion dates first; it is
folded to one line ("Record 38% complete · optional") until opened, and its buttons are quiet
(`bd-btn--optional`). Neither points nor a gap count are shown on the desk, so it cannot
disagree with the gap count in the Comdt preview.

## What the PO can finish on the desk

All posts go through the existing services, so audit records, notifications and workflow rules
are unchanged. The desk adds its own check that the item is assigned to the signed-in PO.

| Action | Service used | Result |
| --- | --- | --- |
| Project remark | `IRemarkService.CreateRemarkAsync` (Internal remark, Project Officer actor; tagged with the current stage when "Stage is still …" is ticked) | Counts as the remark, as the stage confirmation when tagged, and as progress on a direction |
| Idea note | `ProjectIdeaCommandService.AddNoteAsync` | Note titled "Progress update · date" |
| Task progress | `IActionTaskCollaborationService.AddUpdateAsync` (Comment) | Counts as task progress |
| Submit task for closure | `IActionTaskService.SubmitTaskAsync` | Waits on the assigner |
| Flag task blocked | `IActionTaskService.UpdateStatusAsync` (Blocked, reason required) | Blocker shown in conference |

**Posting without a reload** (added 2026-10-30). The update box, the closure note and the blocker
(`data-bd-ajax`) are posted by `workspace-briefing-desk.js` with `fetch` and the header
`X-Briefing-Desk: fetch`. The same `OnPostDesk*` handlers run the same commands; instead of a
redirect they answer with:
- **Success:** the desk re-rendered by `_ProjectOfficerBriefingDesk` (header `X-Desk-Fragment: 1`),
  which replaces the desk on the page. The readiness rules stay in `BriefingDeskBuilder`; the
  script repeats none of them. The script sends whether the card was ready before the post
  (`X-Desk-Was-Ready`). Only a card the post made ready (`X-Desk-Became-Ready: true`) folds into
  its "ready for conference" group: the group line is highlighted, the next card that needs
  work is open and has focus, and a toast says the item is ready. Any other card (still needs
  work, already ready, or a recently completed project) stays open where it is, highlighted,
  with focus.
- **Refused:** a JSON reason (422, or 403/404), shown as a toast. The typed text stays in its
  box and the button is freed.
- **Anything else** (signed out, a server error): the page is loaded again with a GET. A network
  failure shows a message and keeps the text. A failure while showing an answered post (for
  example while swapping the desk in) also reloads, since the post may have been saved. The
  post is never sent a second time.

Without script the forms are ordinary posts: a refused one returns to the same card with the
typed text back in the box it came from (the update box, the closure note or the blocker, with
that form open; `DeskDraftFieldKey`). The PDC and procurement side panels post to their own
editors as before.

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

**Current-stage PDC form.** When a project's current-stage PDC is missing, and nothing else
would be submitted along with it, "Set PDC" opens a short side panel that first asks whether the
current stage is right, then asks only for the PDC. A PDC that has passed has no form on the
desk. "Nothing else" means no private draft, no submission pending or locked,
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
card nor stops it being ready for conference, and there is no Returned to you panel. Before this change
a return stayed listed until a newer submission was pending or approved, so it stuck when an
Admin or HoD saved the plan directly, the PO discarded the correction draft or withdrew the
resubmission, or the project was completed or handed to another PO.
`tools/dev-data/returned-plans-demo-seed.sql` recreates those cases in a development database.

Approval-routed work (plan dates, actual-date corrections, project details, documents) is
linked to the exact editor. Plan links use `?oc=plan&stage=<code>`, which opens the plan
editor with that stage's planned due date focused. Everything pending a decision (plans, date
corrections, stage changes, project details, documents and tasks submitted for closure) is
listed under **Sent for approval** in the side column, as a record rather than a to-do; an entry
another user sent says who. Only plans, date corrections and submitted tasks also appear as a
done step on the card, and a plan shows there only when it answers a missing PDC.

## Wide screens

The cards follow the width of the desk column (a CSS container query on `.bd-main`), not the
window. From a 68rem column (about a 1920px screen) folded cards sit two to a row and an open
card spans the row, with its remark box and steps side by side; from 100rem (a 2560px screen)
three to a row. The My Workspace shell is capped at 140rem (2240px; `--pm-shell-desk`), shared
by the top bar so the menu rail stays aligned.

## Recently completed projects

Completed projects stay in the Conference view for a while, so the PO may still be asked about
them. They appear in a collapsed **Recently completed** section under the desk, built from the
Conference view's own items (`IsRecentlyCompleted`). It opens by itself when a direction there
still needs an answer. These projects never count towards ready for conference, and have no
remark, stage or timeline checks.

## Workspace menu

The PO menu has five entries: **Briefing desk**, **My conference review**, **Project register**,
**My documents** and **My ERP activity**.

- **Project register** (`view=projects`) is a table built from the desk's own project cards
  (stage and PDC, last update, record health with the gap breakdown, and readiness). The two
  pages cannot disagree. The project name opens the project page in the project's own tab
  (`ProjectTab.Name`), so a second click returns to the tab already open; the "Before you
  brief" link goes to the desk card.
- The Action queue, Assigned tasks, My ideas and Follow-ups views were folded into the desk.
  Their addresses still work and redirect to the desk (`IsFoldedIntoDesk`): tasks to
  `#bd-section-task`, ideas to `#bd-section-idea`, follow-ups to `#bd-week-title`.
- The task and idea registers are linked from the desk's Tasks and Ideas section headings. The
  side panel lists reminders and events for the **next 14 days**.

The previous overview's Command view card and ERP activity heatmap were removed from the landing
page. ERP activity stays under **My ERP activity**.

## Phones

Below 576 px the meters sit in one compact row and the line under the headline is hidden, so
the first card is on the first screen. The workspace rail's glass layers are contained in its folded icon row
(`position: relative`); when it was static they covered the first screen with a blur and made
the page wider than the phone. Unread circulars are counted in the
side column's Reading panel only. The workspace rail, folded, becomes a row of its icons above
the page (`command-workspace.css`, all workspace pages); expanding it opens the usual overlay.
