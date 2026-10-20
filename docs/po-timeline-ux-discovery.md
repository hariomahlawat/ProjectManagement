# Project Officer Timeline: UX discovery and improvement plan

**Status:** Discovery only. Nothing described here is implemented. No application behaviour, view, script, schema or service was changed.

> **Release-relevant defect found during the walkthrough (C7.1).** On current `master`, the "Complete mandatory backfill" modal cannot submit anything, for a PO or a HoD. It always answers "Add at least one stage update before saving." The defect came in with D-3c (`20c05d8a`) and is part of the not-yet-deployed D-2 + D-3 release. It needs its own small fix PR before that release; it is not fixed here.

**Baseline:** `master` at `290a2ef8e6ca30a19f2bd337d3836da6075c5b13` (merge of #2690). All `file:line` references are to that commit.

**Guiding question.** Can a Project Officer (PO) understand the project's status, make the necessary updates, submit them for approval and know what happens next, without knowing how the internal services and approval workflows work?

**Short answer.** Mostly yes for *making* updates, and often no for *knowing what happened next*. The editors are usable and the governance underneath them is sound. The main problems are that outcomes are invisible, the same thing is called different names, and work is lost when something fails. These can be fixed incrementally. A Timeline redesign is not justified by the evidence.

---

## Method and limits

1. **Independent pass first.** I inspected the implementation before reading the earlier observations: `docs/phase-d-release-readiness.md` §10, `docs/timeline-proposal-roadmap.md` and `docs/timeline.md` rules 22–27. Four read-only code reviews ran in parallel, one per area: Overview/Timeline, planned dates, actual dates and backfill, and stage requests and outcomes. I then checked the claims that carry the most weight myself. Section D compares the results with the earlier material.
2. **Disclosure.** The author of this report also implemented parts of Phase D-3. Some familiarity with the actual-date editor was unavoidable, so the independent pass deliberately weighted the planning, stage-request and outcome-tracking journeys equally.
3. **Browser evaluation.**
   - The application ran locally against the existing development database on port 5433, with the owner's approval. Startup applied no migrations (119 of 119 already recorded). The development seeders created the test accounts `test_project_offr` and `test_hod`.
   - I signed in as the test PO and saw the first-sign-in forced password change and the PO Workspace. The test PO had no assigned projects.
   - The test HoD's first-sign-in password change was at first refused by the session's permission policy. After the owner approved it, I completed it, then created a **dummy project** ("UX TEST - Dummy timeline project") with `test_hod` as HoD and `test_project_offr` as PO.
   - I gave it a realistic history using the HoD's own stage actions: three stages completed with dates, one completed without a date (backfill needed), one completed through project creation, and one in progress. I also published a partial plan.
   - I then walked through every PO journey as the test PO: plan draft, submit, revise while pending, actual-date correction, backfill and stage update. The HoD rejected the plan, the correction and the stage update with notes, then approved a resubmitted plan, and I checked what the PO saw at each step. I looked at desktop (1440×900), the browser pane's own width (about 900×650) and phone width (375×812). [C7](#c7-walkthrough-with-dummy-data) has the results.
   - C1–C5 were written from the code first. Where the walkthrough confirmed or settled an item, C7 says so.
   - Dates were entered through the page's own fields. Some clicks were sent by script because the pane scales screenshots. Every result reported in C7 was re-checked against the page state or the code.
   - **The owner then supplied five screenshots** of the Overview Timeline pane from their own local instance. They show a 15-stage project at desktop width (about 1920×1080), **viewed by the project's HoD**. [C6](#c6-observed-in-the-browser-owner-screenshots) records what they show. PO-only controls and narrow widths are still unobserved. Project and personal names are deliberately left out of this report.
   - The test accounts, the dummy project (Overview id 214) and its plan, correction and stage-request history were left in the development database on purpose, at the owner's request. Test passwords are kept outside the repository and are not in this report.
4. **What "confirmed" and "observed" mean.** "Confirmed" means the behaviour follows directly from code at the cited lines. "Observed" means it was seen in the running application: the owner's screenshots (C6) or my own walkthrough with dummy data (C7).

---

## A. Executive assessment

### What works

- **Governance is enforced on the server.** Role checks, proposal ownership, approval and stale-decision protection do not depend on hidden buttons:
  - `RequestChange.cshtml.cs:17`
  - `StageRequestService.cs:189-191`
  - `ActualWriteAuthority`
  - the planner's action and authority list (`EditPlan.cshtml.cs:980-992`)
- **The PO can keep working while a request is pending.** This holds for planned dates and actual dates (`PlanReadService.cs:271`; the actual-date editor revises the pending revision).
- **Pending state is explained inside each editor.** The banners say what is waiting and that official values are unchanged:
  - `_EditPlanForm.cshtml:23-29`
  - `_EditActualsForm.cshtml:46-51`
  - the per-stage pending panel on the timeline (`_ProjectTimeline.cshtml:586-606`)
- **The actual-date editor shows the official values** beside each proposed value (`_EditActualsForm.cshtml:222-225`). It warns when official dates changed after submission, and when a proposal is already satisfied.
- **The plan planner** autosaves with a live status (`plan-edit.js:18-22`) and has an explicit draft lifecycle with Discard.
- **The off-canvas editors reflow to one column on small screens** (`project-portfolio.css:1411-1431, 1484-1487`).

### What causes friction

1. **Outcomes are invisible from the Overview.** A rejected stage update leaves no trace for the PO. Rejected plans and rejected actual-date corrections can be found only by opening the relevant editor. A pending actual-date correction does not appear on the Overview at all. Several outcomes send no notification.
2. **There is one confirmed visibility defect.** A PO who has an unsubmitted plan draft loses the Timeline menu items for both planned and actual dates.
3. **Some feedback is misleading.**
   - Saving a plan draft while a submission is pending can silently cancel the HoD review; the message only says "Draft saved."
   - The backfill confirmation disappears on reload, and the banner keeps saying "Action needed".
4. **Typed work is lost on failure.** Planner and actual-date validation failures redirect, throwing away what was entered. The error appears behind the editor panel, and one message refers to "highlighted errors" that are never highlighted.
5. **The stage that needs action is far down the pane (observed).** On a typical project the Timeline lists every completed stage before the current one. The current stage was the 14th card, several screens down, and nothing scrolls to it. On a phone the Timeline itself starts about 4.7 screens down the page.
6. **Two input defects were found in the walkthrough.**
   - The backfill modal cannot submit at all (C7.1).
   - In the actual-date editor, a valid-looking correction is blocked by a browser tooltip. The page's own explanation, which exists in the script, is never shown (C7.2).
5. **Vocabulary and interaction patterns are inconsistent.** There are four submit verbs and three withdraw placements. The approver is named in two ways, and one editor has three names. Raw codes and enum values (`InProgress`, stage codes) appear in user-facing text.

### Verdict: incremental refinement, not a redesign

The structure is sound: one timeline, per-stage actions and three focused editors whose rules match the business categories. Almost every significant problem is about **state visibility, feedback, wording or error recovery**. Each can be fixed inside the existing Razor partials and scripts, with read-model changes at most and no schema change.

The larger unification described in `docs/timeline.md` rule 27 (a single **Update** experience over a Timeline Change Set) depends on Phase E. It should not be started to solve these problems, and nothing below conflicts with it.

---

## B. Current user journey

| Journey | Steps today | Where friction occurs |
| --- | --- | --- |
| **A. Understand the timeline** | Open the project. The header shows "Current stage". The timeline rail shows status pills; completed rows expand to show dates. | The header detail line shows the raw stage code (`ProjectPortfolioPresentationVm.cs:77`). Completed-row dates are hidden until the row is expanded (`project-portfolio.css:524-529`). Blocked and Skipped stages show no date, reason or note (`_ProjectTimeline.cshtml:608-713`). Skipped and Not started share the same grey and the rail dots rely on colour alone (`_ProjectTimeline.cshtml:37-41`). Draft and pending planned dates never appear on the timeline (`ProjectTimelineReadService.cs:280-282`). Below 74rem the timeline column renders **after** the overview, procurement and media cards (`project-portfolio.css:2134-2135`). |
| **B. Update planned dates** | Timeline ⋮ → "Edit planned dates". Choose the **Exact dates** or **Durations** tab. Then use "Calculate" (Durations only), "Save draft" or "Save & request approval". Withdraw from the timeline header. | "Calculate" is a full save-and-reload rather than a preview (`EditPlan.cshtml.cs:745-755, 831`). An explicit "Save draft" while a submission is pending supersedes the HoD review (`EditPlan.cshtml.cs:375-376, 759-760`), but the message only says "Draft saved." (`:454, 827`). Errors throw away typed values (`:934-966`). The rejection reason is visible only inside the editor (`_EditPlanForm.cshtml:46-62`); on the card, a returned plan just reads "Draft saved (not submitted)" (`_ProjectLifecycleWorkspace.cshtml:114-117`). Withdraw has no confirmation (`:102-107`). |
| **C. Update actual dates** | Timeline ⋮ → "Correct recorded dates". Edit the dates, clear a start by emptying it, optionally add a reason, then "Submit for approval". Revise by submitting again, or use "Withdraw pending update". | The editor shows proposed values, with the official value in small muted text (`_EditActualsForm.cshtml:112, 222-225`). Clearing a date has no visible control; it happens implicitly when the field is emptied (`actuals-edit.js:157-161`), although the intro talks of an "explicitly cleared" date (`_EditActualsForm.cshtml:41`). After a rejection the PO must retype everything: the rejected values are not shown (`ActualsEditorVm.cs:32-45`). A failed post loses typed values (`EditActuals.cshtml.cs:303-308`). |
| **D. Backfill missing dates** | Banner "Action needed …" or the "Backfill required" pill opens "Complete mandatory backfill". Enter the completion date (required) and, optionally, the start. "Submit for approval". | It does the same operation as the editor, with different wording, no reason field and no way to clear the start (`_BackfillForm.cshtml`; `BackfillViewModel.cs:114`). The success toast is lost to a reload after 500 ms (`overview.js:1135-1138`), and the banner still says "Action needed" (`_ProjectOperationalState.cshtml:60-74`). |
| **E. Progress and lifecycle** | "Update stage" (current stage) or ⋮ (other stages) opens the stage update modal. Choose "Update to", the date and the note, then "Submit update". Editing a pending request supersedes it. | There is no withdraw (`StageRequestService` has none). A rejection is never shown to the PO, and neither is the HoD's note (`ProjectTimelineReadService.cs:150-156` loads only Pending and Superseded requests; `StageDecisionService.cs` writes only a log row). The modal says "This update will be visible immediately" next to "official … only after HoD approval" (`_StageRequestModal.cshtml:28-29`). The PO is offered "Complete stage directly", which is still a request (`stages.js:972`). |
| **F. Return after an absence** | Workspace action queue, the notification bell, then each project's Overview, then each editor. | There is no single place for "my pending and returned items". The Workspace "Returned" list covers 30 days, shows 5 items, **excludes actual-date corrections**, titles stage items with raw codes and hides them when an unrelated stage request exists (`ProjectOfficerWorkspaceService.cs:1019-1096`). The Decision Centre is HoD/Admin only (`Pages/Approvals/Pending/Index.cshtml.cs:18`). |

### Can a returning PO tell these states apart?

| State | Planned dates | Actual dates | Stage status |
| --- | --- | --- | --- |
| Action required now | Timeline badges ("Planned completion missing") | "Action needed" banner and "Backfill required" pill | Overdue badge |
| Draft not submitted | Card badge "Draft saved (not submitted)" | No drafts (by design) | No drafts (by design) |
| Awaiting approval | Card badge and banner | **Only inside the editor** | Per-stage panel and header count |
| Rejected, needs correction | Inside the editor, plus Workspace "Returned" (30 days) | **Only inside the editor** | **Nowhere on the project.** Workspace "Returned" shows only the stage code, with no reason |
| Approved and reflected | "Approved on …" badge and notification | Nothing; the dates simply change | A generic "stage completed" notification that never says "approved" (`StageNotificationService.cs:82-92`) |

---

## C. Independent findings

**Frequency** is my estimate from how often the workflow is used, not measured data.

### C1. Outcome visibility

| # | Current behaviour (confirmed) | Why it hurts | Frequency | Impact |
| --- | --- | --- | --- | --- |
| C1.1 | A stage-request rejection is silent: no notification, no on-page trace, and the HoD's note is never shown to the PO (`StageDecisionService.cs:174-212`; `ProjectTimelineReadService.cs:150-156`). The pending panel simply disappears. | The PO cannot tell whether the request was approved, rejected or lost, or why. | Every stage rejection | High: rework or a wrong belief about project status |
| C1.2 | A pending actual-date correction is not shown anywhere on the Overview. Its rejection is shown only inside the editor (`Overview.cshtml.cs:2465-2499`). There is no notification type for it (`Models/Notifications/NotificationKind.cs`). | The PO must open the editor just to learn the status. | Per correction | Medium–High |
| C1.3 | A plan rejection's reason is visible only inside the planner. The card shows "Draft saved (not submitted)", and the rejection notification omits the reason (`PlanNotificationService.cs:85-111`). | A rejection looks like an ordinary draft. | Per rejection | Medium |
| C1.4 | Stage approval is announced as a generic status change (`StageNotificationService.cs:82-92`). | "Approved" is never stated. | Per approval | Low–Medium |
| C1.5 | The "Lifecycle position" card shows "Approved" whenever the plan was ever approved, even while a newer submission is pending (`ProjectPortfolioPresentationVm.cs:168-170, 374-377`). | Two parts of the page disagree. | Common after the first approval | Medium |
| C1.6 | **Likely defect, verify first.** The planner's "Returned for updates" banner falls back to the user's most recent Rejected version of any age (`PlanReadService.cs:96-100, 277-279`). It can therefore appear again after a later resubmission is approved and no draft remains. | It shows a stale rejection as current. | After any past rejection | Medium (misleading) |

### C2. Discoverability and misleading feedback

| # | Current behaviour (confirmed) | Why it hurts | Frequency | Impact |
| --- | --- | --- | --- | --- |
| C2.1 | `canEditTimelinePlan = canEditTimeline && !(hasMyDraft && !pendingOwnedByCurrentUser)` (`_ProjectLifecycleWorkspace.cshtml:215`) hides **both** "Correct recorded dates" and "Edit planned dates" when the PO has an unsubmitted plan draft. Only the "Continue editing" chip remains. | Having a plan draft locks the PO out of the actual-date editor's main entry point. That is unrelated to the plan, and the roadmap rules forbid it (rule 22). | Common for active planners | **High** |
| C2.2 | An explicit "Save draft" while the PO's plan submission is pending supersedes it. The PO is told only "Draft saved." (`EditPlan.cshtml.cs:375-376, 454, 759-760, 827`). Only the HoD is notified. | The PO believes the HoD is still reviewing; in fact nothing is under review. | Common during revisions | **High** (wrong belief about status) |
| C2.3 | After a PO backfill submission, the toast is lost to a reload after 500 ms (`overview.js:1135-1138`). The banner and pill still say "Action needed" and "Backfill required". | The PO may submit again, or think it failed. | Every PO backfill | Medium–High |
| C2.4 | The stage modal says "This update will be visible immediately" beside "official … only after HoD approval" (`_StageRequestModal.cshtml:28-29`). The PO's option is labelled "Complete stage directly" (`stages.js:972`). | Contradictory statements about authority. | Every stage update | Medium |
| C2.5 | The hint "Use Edit actual dates to correct it separately" (`stages.js:729, 1064`) names a menu item that does not exist; it is "Correct recorded dates". The backfill tooltip says "Resolve procurement backfill" (`_ProjectLifecycleWorkspace.cshtml:121`). | It sends the user looking for something that isn't there. | Occasional | Low–Medium |
| C2.6 | The "Open timeline" and "Current stage" shortcuts only scroll the page. If the panel is showing Remarks (the default for terminal projects), the timeline stays hidden (`project-portfolio.js:53-57`; `overview.js:1377-1396`). | It looks like a dead click. | Occasional | Low |
| C2.7 | The "Planned completion missing" and "Set planned completion" links open the whole planner and don't focus the stage concerned (`_ProjectTimeline.cshtml:201-208, 692-698`). | A one-field fix becomes a search. | Common | Low–Medium |

### C3. Editing and error recovery

| # | Current behaviour (confirmed) | Why it hurts | Frequency | Impact |
| --- | --- | --- | --- | --- |
| C3.1 | Planner validation failures redirect to the Overview, rebuild the panel from the database and drop the typed values (`EditPlan.cshtml.cs:934-966`). A failed Exact "Save & request approval" rolls back the typed dates (`:413-446`). All messages are joined into one alert that says "Please correct the highlighted errors", but nothing is highlighted (`:942-944`). | A whole plan may have to be re-entered. | Occasional | **High** when it happens |
| C3.2 | The same applies to a failed server-side post in the actual-date editor (`EditActuals.cshtml.cs:303-308`). | Re-entry. | Occasional | Medium |
| C3.3 | Errors render on the page behind the reopened panel (`_ProjectOperationalState.cshtml:35-41`). **Hypothesis:** on narrow screens the panel (up to 100vw) hides them completely. | The PO may not see why the save failed. | Per error | Medium |
| C3.4 | A start date is cleared by emptying the field. There is no visible control, and the meaning depends on whether an official start exists (`actuals-edit.js:157-161`). | "Blank" can mean either "keep" or "clear". | Occasional | Medium |
| C3.5 | After a rejection the editor shows official values only; the rejected values are not shown (`ActualsEditorVm.cs:32-45`). | The PO must remember or retype the rejected proposal. | Per rejection | Low–Medium |
| C3.6 | The planner can lose work in several ways: Exact mode has no autosave, closing the panel cancels a queued autosave (`plan-edit.js:135-141`), switching tabs drops the other tab's unsaved form, and there is no unsaved-changes warning. | Silent loss. | Occasional | Medium |
| C3.7 | **Hypothesis.** The planner defaults to the Durations tab whenever an anchor date exists (`PlanReadService.cs:119, 249-252`), and Exact saves never clear it. So an Exact user may be returned to Durations on every reopen. | Disorienting. | Common for Exact users | Low–Medium |
| C3.8 | The draft timestamp labelled "Your last save" shows when the draft was created, not when it was last saved (`PlanReadService.cs:256`). | It looks as if the last save didn't happen. | Common | Low |

### C4. Consistency and language

- **Submit and withdraw.** The verbs are "Save & request approval" (plan), "Submit for approval" (actual dates and backfill) and "Submit update" / "Save update" (stage). Withdraw is in the timeline header with no confirmation for plans, in the editor footer with a confirmation for actual dates, and absent for stage requests.
- **Approver.** The text says "HoD" for plans and stages but "HoD or Admin" for actual dates. Admin can in fact also decide stage requests (`DecideChange.cshtml.cs:18`).
- **One editor, many names.** The actual-date editor is called "Correct recorded dates", "Edit actual dates", "Record actual stage dates" and "actual-dates editor".
- **One submitted thing, many names.** It is called a correction, revision, pending update, proposal, submission or transition.
- **Backfill.** It is called "Backfill required", "Needs backfill", "Complete mandatory backfill", "completion details" and "backfill pending".
- **"Inferred"** means two different things (auto-completed vs estimated start).
- **System wording in user text.** For example:
  - `InProgress` and `NotStarted` (`_EditActualsForm.cshtml:41, 138`)
  - "Submitted transition: Completion submitted" (`_ProjectTimeline.cshtml:592`)
  - "revision #N (ID x)" and "stamp and snapshot" (`_ReviewPlan.cshtml:28, 64`)
  - "supersedes the existing review" (`_EditPlanForm.cshtml:23-29`)
  - stage codes in notices (`_EditActualsForm.cshtml:60-62`; `ProjectOfficerWorkspaceService.cs:1048-1051`)
- **Same badge, different wording.** "Lifecycle update pending" (PO) vs "Pending approval" (HoD), and "Proposed correction" vs "Proposed, awaiting approval".

### C5. Accessibility and responsive behaviour

All of these are confirmed in markup or code; their effect on assistive technology needs testing.

- **Duration inputs** have no label, id or `aria-label`, so screen readers get no stage name (`_PlanDurationRow.cshtml:18-23`).
- **Exact-row labels** are the same on every row.
- **Stage-update modal labels** are not linked to their inputs (`_StageRequestModal.cshtml:60-90`).
- **Errors.** No `aria-invalid`, row errors are not linked to their inputs (`actuals-edit.js:64-77`), and focus does not move to errors.
- **Headings.** The Timeline heading is an h5 after h3 cards (`_ProjectLifecycleWorkspace.cshtml:80`). Stages are divs with no headings or list semantics.
- **Colour and tooltips.** Rail dots and changed review rows use colour alone. Several explanations exist only as `title` tooltips ("Inferred", "ARPP-derived", the variance chip).
- **Misplaced ARIA.**
  - `aria-label` on plain `<span>`s (`_ProjectTimeline.cshtml:173, 229, 649`).
  - Both `aria-pressed` and `aria-expanded` on the Timeline/Remarks toggle (`_ProjectLifecycleWorkspace.cshtml:196-197`).
  - The record-health progress bar has no label (`_ProjectRecordCompleteness.cshtml:27`).
- **Responsive.** In the single-column layout (below 74rem) the timeline renders after several other cards. The timeline column has a 31rem minimum width (`project-portfolio.css:2071, 2134-2135`). **Hypothesis:** on tablets and phones the timeline is hard to reach.

### C6. Observed in the browser (owner screenshots)

These are from the HoD view at desktop width, so the PO's visible "Update stage" button and proposal banners are not shown. Each item is checked against the code.

**What works well**
- Status pills carry text ("Completed", "In progress", "Not started"), not colour alone.
- Collapsing completed stages keeps each card short.
- The in-progress stage shows "Started on" next to an amber "Planned completion" panel with a direct "Set planned completion" link. That is a clear call to action.
- "Later stages" is a labelled group with a short explanation ("Planned dates may be added when available.").
- Dates use one format throughout (`dd MMM yyyy`).

| # | Observed | Why it hurts | Code | Priority |
| --- | --- | --- | --- | --- |
| C6.1 | At 1920×1080 the pane's first stage card begins at the bottom of the first screen, below the header card and the backfill banner. The **current stage is the 14th of 15 cards**: it comes after 12 collapsed completed stages and a backfill-pending stage, several screens down. Nothing scrolls to it when the page opens; scrolling to a stage only happens for deep links (`overview.js:1411-1479`). | "What needs my attention?" is the main question, and the answer is at the end of a long list of finished work. Only the header card ("Current stage") answers it at a glance. | `_ProjectTimeline.cshtml` renders in workflow order | **P1** |
| C6.2 | **One missing completion date is described seven ways on one screen:** "1 stage require backfill" (header), "1 completed stage requires completion details" (banner), "Complete mandatory backfill" (button), "Backfill required" (grey badge in the Timeline header, amber pill on the stage), "Completion date required" (stage meta) and "1 backfill pending" (progress line). The same concept is grey in one place and amber in another. "1 stage require backfill" is also ungrammatical. | The user can't tell whether these are one task or several. The word "backfill" means nothing to an ordinary user. | `ProjectPortfolioPresentationVm.cs:71, 86`; `_ProjectOperationalState.cshtml:60-74`; `_ProjectLifecycleWorkspace.cshtml:121, 298`; `_ProjectTimeline.cshtml:186-195` | P1 (wording; part of 3a) |
| C6.3 | The header detail line reads "PAYMENT · 1 stage require backfill": a raw stage code next to the stage name. | Confirms C4. | `ProjectPortfolioPresentationVm.cs:77, 86` | P2 |
| C6.4 | **The date and duration on a collapsed card have no label**: for example "13 May 2025 · 22 days". Only the expanded view says these are "Completed on" and "Time taken: 22 calendar days". Where they sit depends on the length of the stage name: on the same line for short names, on a second line for longer ones. So card heights differ and the dates don't line up down the list. The expanded panel lists "Completed on", "Time taken", "Actual start", which is the reverse of the order the events happened. | The user cannot scan the dates down the list, and may read the date as the start or the planned date. | `_ProjectTimeline.cshtml:610-650`; `project-portfolio.css:524-529` | P2 |
| C6.5 | On the in-progress card the badges come before the name ("In progress · Planned completion missing · Payment"). On every other card the name comes right after a single pill. The missing planned completion is flagged twice on the same card: the badge and the amber panel. | The stage name moves around; the duplicate signal adds noise without adding information. | `_ProjectTimeline.cshtml:201-208, 692-698` | P2 |
| C6.6 | The Timeline header shows "Approved on 17 Dec 2025" beside "Backfill required", while the current stage has no planned completion. | "Approved" can be read as "the plan is complete". In fact it only means a plan was approved on that date. | `_ProjectLifecycleWorkspace.cshtml:130-133` | P2 (wording: "Plan approved on …") |
| C6.7 | In the HoD view, every edit action in the pane is behind ⋮ menus: the Timeline header menu and one per stage. Nothing in the pane says what can be done. | Discoverability. The PO view has a visible "Update stage" on the current stage (`_ProjectTimeline.cshtml:244-267`); that still needs a PO walkthrough. | `_ProjectLifecycleWorkspace.cshtml:220-275` | P2 |
| C6.8 | **Remarks tab: a failed load looks like an empty list.** A red toast says "Unable to load remarks." while the panel says "No remarks yet." and still offers "View all remarks". This is confirmed in code: the `finally` block's `setLoading(false)` writes "No remarks yet." whenever no items are loaded, including after an error (`remarks-panel.js:1143-1146, 1185-1207`). | The user may conclude that the project has no remarks, which is wrong. Why the load failed in the owner's instance was not investigated. | `remarks-panel.js` | **P1** (misleading). It is on the Timeline pane, though not part of the Timeline workflow |
| C6.9 | The "Lifecycle position" card in the left column has an "Open timeline" button, although the Timeline pane is already visible beside it at desktop width. | Duplicated navigation. On a wide screen it only scrolls (C2.6). | `_ProjectLifecyclePosition.cshtml:12`; `project-portfolio.js:53-57` | P2 |

Unrelated to the Timeline, but seen in the same screenshots: the project's two photo thumbnails render as broken images showing their alt text, and the header card says "Cover photo unavailable". This should be raised as a separate issue.

### C7. Walkthrough with dummy data

The dummy project was walked through as the PO and the HoD on `localhost:5199`, against master `290a2ef8`. Each row names the step that showed it.

#### New findings

| # | Observed | Cause (confirmed in code) | Priority |
| --- | --- | --- | --- |
| **C7.1** | **The backfill modal never submits.** The PO entered the AON start and completion dates and clicked "Submit for approval"; the modal answered "Add at least one stage update before saving." The HoD's "Save" in the same modal failed the same way. The banner and pill keep saying "Action needed" / "Backfill required" indefinitely. | `_BackfillForm.cshtml:14` renders `data-backfill-carried="@(stage.IsCarriedForward ? "true" : null)"`. ASP.NET Core Razor still renders a `data-*` attribute when its value is null, as `data-backfill-carried=""` (seen in the DOM on a row that is not carried forward). `overview.js:1056` excludes every row for which `hasAttribute('data-backfill-carried')` is true, so the payload is always empty. The actual-date editor's similar flag works, because it compares the value (`actuals-edit.js:227`, `=== 'true'`). Introduced in D-3c (`20c05d8a`). No test covers it: the acceptance tests post JSON directly, and no JavaScript test renders this markup. | **P0, release-blocking for D-2 + D-3.** Separate fix PR: test the attribute's value, and add a regression test that uses server-rendered markup |
| **C7.2** | **The actual-date editor silently blocks a coordinated correction.** The PO moved the SOW completion from 10 Feb to 12 Feb. The next stage's recorded start (11 Feb) then became earlier than allowed. "Submit for approval" was enabled, but clicking it did nothing visible except move focus to the IPA start field. The only explanation is the browser's native tooltip, "Value must be 12-02-2026 or later." After the PO also set the IPA start to 12 Feb, the submit worked (revision 1). | `refreshChronologyBoundaries` sets `min` on every row (`actuals-edit.js:125-137`), but `validateRow` returns early for rows the user has not changed (`:156-158`). So the script's own message ("Actual dates cannot precede SOW Vetting completion … Same-day commencement is permitted.", `:190`) never appears, and native constraint validation blocks the submit. | **P1.** Validate the affected later rows as well, and show the existing message on them |
| C7.3 | **The editor shows about two stages at a time.** The actual-date editor's stage list is its own scroll box inside the panel: 460px high with 207px stage cards at 1440×900, and about 170px at about 900×650. With 15 stages, finding a row means scrolling a small box inside a panel. The reason field and buttons stay fixed below it. | Layout of `_EditActualsForm.cshtml` inside the off-canvas | P1 (part of Increment 2) |
| C7.4 | **Creating an "ongoing" project gives an incoherent timeline.** With "last completed stage = Bidding/Tendering, 15 Jun 2026", only Bidding was recorded, as completed with start = completion ("1 day"). Every earlier stage stayed "Not started", the current stage was shown as Feasibility Study, and all 15 stages appeared under "Later stages". | `Create.cshtml.cs:327-342` inserts one stage row and nothing for its predecessors | P2. This is project creation rather than the PO Timeline, so raise it as its own issue |
| C7.5 | After the PO saved a draft that gave the current stage a planned completion date, the stage card still said "Planned completion missing" / "Set planned completion". | Correct under rule 23 (drafts are not official), but nothing tells the PO that their draft holds a value | P2 (see E.1 status line) |
| C7.6 | At about 900px width the collapsed card's date, separator and duration stack on three lines ("10 Feb 2026 / · / 21 days"). | `_ProjectTimeline.cshtml` meta layout | P2 (3e) |
| C7.7 | An empty backfill submit says "Add at least one stage update before saving.", although "stage update" means a status request everywhere else and the button says "Submit for approval". | `overview.js` | P2 (3a) |
| C7.8 | The HoD's plan review lists all 15 stages, including unchanged ones ("— → —"), and labels the submitted plan "Draft plan". It calls a missing stage completion date "procurement backfill pending … procurement details" (`_ReviewPlan.cshtml:57`). | Wording and layout | P2 (HoD side; wording in 3a) |

#### Code findings settled by the walkthrough

| Earlier item | Result |
| --- | --- |
| C2.1 hidden menu | **Confirmed.** After the PO's "Save draft", the Timeline header had no ⋮ menu at all, only "Continue editing" and "Discard draft". "Correct recorded dates" was unreachable from the Timeline. |
| C2.2 silent supersession | **Confirmed.** "Save draft" while the plan was pending turned "Timeline plan submitted for approval" into "Draft saved (not submitted)". No message said the HoD review had ended. |
| C2.3 backfill feedback | Overtaken by C7.1: the submission itself never happens. |
| C1.1 stage rejection | **Confirmed.** After the HoD rejected the stage update with a note, the Technical Evaluation card showed a plain "Update stage" again. The Overview had no trace of the rejection and no note. The bell had no notification. The Workspace showed "Stage update returned by HoD" under a group headed with the raw code "TEC", without the project name or the note. |
| C1.2 actual correction | **Confirmed.** After the submission and a reload, nothing on the Overview mentioned the pending correction. The plan, by contrast, was shown three times. After rejection, the remarks appear only inside the editor. The rejection is not in the Workspace "Returned" list and there is no notification. |
| C1.3 plan rejection | **Confirmed.** The bell says "Plan version 2 rejected." with no reason. The card shows "Draft saved (not submitted)". The reason appears only in the planner. |
| C1.6 stale rejection banner | **Confirmed.** After the resubmitted plan was approved ("Approved on 20 Oct 2026"), the planner still showed "Returned for updates … Note: … allow two more weeks." |
| C3.1 typed values lost | **Confirmed** on the HoD's direct save. A plan with 14 dates failed the next-working-day rule. The panel reopened with every date empty, and the error appeared behind the panel. |
| C3.5 rejected values | **Confirmed.** After the rejection the editor showed the official 10 Feb; the rejected 12 Feb was not shown. |
| C3.8 "last save" time | **Confirmed, and more confusing than expected.** After the rejection the planner said "Your last save was on 20 Oct 2026 14:16". That is the time the rejection created the correction draft; the PO did not save at 14:16. |
| C3.7 Durations tab default | Not reproduced. This project had no anchor date, so Exact was the default. Still unverified. |
| C5 identical labels | **Confirmed** in the accessibility tree. There are 20 fields named only "Planned start" or "Planned due", with no stage name, and the "Completed on" fields in the editor are the same. |
| C5 responsive | **Confirmed.** At about 900px the Timeline appears only after the Videos card. At 375px it starts 3,796px down the page (about 4.7 screens). There is no horizontal scroll. |
| C2.4 / C2.5 modal copy | **Confirmed.** The stage modal says "This update will be visible immediately." and "Use Edit actual dates to correct it separately." Its stage list shows "FS — Feasibility Study"-style codes. |
| Working as intended | The PO's visible "Update stage" on the current stage; the stage pending panel (proposed dates, submitter, note); the plan "Withdraw" and draft chip; the correction pending and remarks banners; the HoD's correction review, which shows recorded vs proposed values and the PO's justification. |

---

## D. Comparison with previous findings

This section was written after the independent pass. The sources are `phase-d-release-readiness.md` §10, `timeline-proposal-roadmap.md` and `timeline.md` rules 22–27.

| Earlier observation | Assessment |
| --- | --- |
| §10.3. The editor sits under Timeline → "Correct recorded dates"; backfill is separate. | **Confirmed and widened.** The real defect is C2.1: the entry point disappears when a plan draft exists. That is the roadmap's open **G02**, but its user impact was not recorded there. |
| §10.4. A blank start in proposal mode means "keep", so reverting needs retyping. | **Confirmed, refined (C3.4).** The root cause is the absence of a visible clear control, not the reverting itself. An explicit control fixes both, and also gives a browser path that works without JavaScript. |
| §10.5. Typed values are lost after a stale or invalid post. | **Confirmed and higher priority than recorded.** The same happens in the planner, where a whole plan can be lost (C3.1), and the error appears behind the panel. |
| §10.6. Editor and backfill use different interaction models. | **Confirmed, refined.** The model difference (form post vs JSON) is invisible to users. What they experience is different words, no reason field, no clear option and lost feedback (C2.3). Fix the behaviour; the transport can stay. |
| §10.7. Rejection is visible only in the editor; no notification. | **Confirmed, and too narrow.** It applies to all three categories, and stage requests are worse: their rejection is never shown at all (C1.1). |
| §10.1. Decision Centre readiness vs pending lifecycle requests. | A HoD-side issue, not part of the PO journey. Lower priority than recorded for this exercise. |
| §10.2. Generic approval confirmation. | Already improved; HoD-side. |
| Roadmap **G14**: four separate choices (plan, actual correction, stage update, backfill). | **Partly agreed.** The separate choices match real business categories, and users can tell them apart. The problem is the inconsistent behaviour and wording *between* them, not that there are four. |
| Rule 27, target experience: one **Update** action over a Timeline Change Set. | **Not contradicted, but not needed to fix today's problems.** It depends on Phase E (G05). The increments below are prerequisites that remain valid under that model: shared wording, a status summary and preserved input. |
| Not in the earlier material | C1.1 (silent stage rejection), C1.5 (contradictory "Approved"), C1.6 (stale rejection banner), C2.2 (silent supersession), C2.3 (lost backfill feedback), C2.4 (contradictory modal copy), C2.5 (wrong names in hints), C3.6–C3.8 (planner work loss and timestamp), C5 (accessibility). |

---

## E. Recommended experience

This describes what the PO should see and read, not how to build it.

### 1. One status line per category in the Timeline card

The Timeline card header already shows plan badges. It should show a compact summary of the PO's own items, one line per category, only when the category has something to show:

> **Planned dates:** Awaiting HoD approval (submitted 14 Oct). · View
> **Actual dates:** Correction returned on 16 Oct: "IPA start conflicts with SOW." · Review
> **Stage updates:** Completion of SOW returned on 17 Oct: "Attach trial report first." · Review

- Each "View" or "Review" link opens the existing editor or modal.
- After approval, show "Approved on …" briefly, until the next submission or for a fixed number of days. Then show nothing.
- This reuses the existing badge area and data that is already loaded. The stage-request rejection needs a read of the latest decided request per stage. That is read-only; no schema change is needed.

### 2. Say what happened, in the user's words

- **A plan save that supersedes a pending submission:**
  > Draft saved. Your earlier submission is no longer with the HoD; submit again when ready.

  Give the plain "Draft saved." only when nothing was superseded.
- **Backfill or correction submitted:** keep the confirmation visible by putting it in the page flash after reload. Change the banner state to:
  > Submitted for approval. Official dates update when a HoD or Admin approves.
- **Stage update modal:**
  > Your update is sent to the HoD for approval. The timeline shows it as pending; official status changes only after approval.

  Label the PO's options as requests: "Request completion", "Request start".

### 3. Don't lose what I typed

- On a validation failure, reopen the editor **with the typed values**.
- Show the error **inside the editor**, next to the field when it belongs to a field, with focus moved to the first error.
- Remove "highlighted errors" text that nothing backs up.

### 4. Make official vs proposed obvious, and make clearing explicit

In the actual-date editor, each row should show an **Official** value and a **Your proposal** field side by side. Add a "Clear recorded start" checkbox, shown only where clearing is allowed. That keeps the explicit clear semantics and works without JavaScript.

After a rejection, show "Rejected proposal: …" as a read-only reference line under each affected row.

### 5. One vocabulary

| Concept | Use | Retire |
| --- | --- | --- |
| Planned dates editor | **Planned dates** ("Edit planned dates") | "timeline plan", "draft plan" (for submitted content) |
| Actual dates editor | **Actual dates** ("Correct actual dates") | "recorded dates", "Edit actual dates", "actual-dates editor" |
| Stage status change | **Stage update** | "transition", "lifecycle update" |
| Submit (all PO flows) | **Submit for approval** | "Save & request approval", "Submit update" |
| Waiting | **Awaiting approval** (naming who can approve where it matters: "HoD", or "HoD or Admin") | "under HoD review", "pending update" |
| Returned | **Returned** with the reviewer's note | "Rejected" in PO-facing text, "Returned for updates" |
| Take back | **Withdraw** (always with a confirmation) | — |
| Missing completion date | **Completion date required** (as `timeline.md` rule 22 already says) | "backfill" in PO-facing text |

Show stage **names** instead of codes in notices, and status labels ("In progress") instead of enum values. HoD- and Admin-facing review screens may keep codes beside names.

### 6. Quick paths stay

Keep the per-stage "Update stage" button and the backfill banner as shortcuts. They must open the same behaviour, with the same wording, as the main editors. "Planned completion missing" should open the planner focused on that stage.

---

## F. Prioritised improvement plan

There are four increments. Each one can be shipped and tested on its own. None changes authorization, approval rules, revision history, explicit-clear semantics or stale-decision protection.

### Before Increment 1: fix the backfill modal (P0, separate PR)

C7.1 is a defect, not a UX refinement. It blocks PO and HoD backfill through the modal on `master` and belongs to the pending D-2 + D-3 release. The fix is to compare the attribute's value (`=== 'true'`), or to omit the attribute when the row is not carried forward. It needs a regression test that runs the modal script against server-rendered markup. Size: Small. Risk: Low.

### Increment 1: Know where you stand (P0 + P1)

| Item | Current → proposed | Screens/components | Backend? | Size | Risk |
| --- | --- | --- | --- | --- | --- |
| 1a. Fix the hidden menu (C2.1) | Separate actual-date visibility from the plan-draft state | `_ProjectLifecycleWorkspace.cshtml:215` | No | Small | Low: the endpoint already authorizes on its own (`ActualWriteAuthority`) |
| 1b. Status summary (E.1) | Pending, returned and recently approved items for all three categories, with notes | `_ProjectLifecycleWorkspace.cshtml`, `OverviewModel`, `ProjectTimelineReadService` (read the latest decided request per stage) | Read model only | Medium | Low. The summary must be scoped to the current user's own items, or to the project when the user may view it |
| 1c. Truthful save and backfill feedback (C2.2, C2.3) | Supersession-aware flash; a persistent post-reload message; a "Submitted for approval" banner state | `EditPlan.cshtml.cs` flash, `overview.js` backfill success, `_ProjectOperationalState.cshtml` | Return whether supersession happened (already known inside the service) | Small | Low |
| 1d. Correct the stale or contradictory states (C1.5, C1.6) | "Approved" only when no newer submission is pending; the rejection banner only for the current lineage. **Verify C1.6 with a test first.** | `ProjectPortfolioPresentationVm.cs`, `PlanReadService.cs` | Read model | Small | Low |
| 1e. Lead with the stage that needs action (C6.1) | Group the completed stages into one collapsible "N completed stages" row, expanded only on request, so the current stage, stages needing a date, and pending items appear first. Keep workflow order inside the group | `_ProjectTimeline.cshtml`, `project-portfolio.js` | No | Small–Medium | Low: presentation only. Deep links (`#timeline-stage-…`) must still open a stage inside the group |
| 1f. Remarks load failure is not an empty list (C6.8) | Show "Remarks could not be loaded. Try again" with a retry, instead of "No remarks yet." | `remarks-panel.js` | No | Small | Low |

### Increment 2: Don't lose my work (P1)

| Item | Current → proposed | Screens/components | Backend? | Size | Risk |
| --- | --- | --- | --- | --- | --- |
| 2a. Preserve typed values on failure (C3.1, C3.2) | Re-render the editor from the posted form, with errors per field | `EditPlan.cshtml.cs`, `EditActuals.cshtml.cs`, the editor partials | Page models only (the TempData round-trip already exists for the HoD concurrency case, `EditPlan.cshtml.cs:141-148`) | Medium | Medium. Re-rendered values must stay *proposed* and never be treated as saved. Covered by tests |
| 2b. Errors inside the editor, focus moved to them (C3.3, C7.2) | Move the alert into the panel; set `aria-invalid`; move focus. In the actual-date editor, also validate the later rows a change affects, so that the existing chronology message appears instead of the browser tooltip | Editor partials, `actuals-edit.js`, `plan-edit.js` | No | Small | Low. Client-side only; the server's final-state validation is unchanged |
| 2f. Show more of the editor (C7.3) | Let the stage list use the panel's full height (one scroll region), or collapse rows that aren't being edited | `_EditActualsForm.cshtml`, `project-portfolio.css` | No | Small | Low |
| 2c. Explicit clear and side-by-side official vs proposal (E.4, C3.4, C3.5) | A "Clear recorded start" checkbox; an official column; a read-only "rejected proposal" line | `_EditActualsForm.cshtml`, `actuals-edit.js`, `ActualsEditorVm` | View model (rejected values are already stored in `ActualCorrectionItems`) | Medium | Medium. The existing posted `ClearActualStart` contract and the validator must stay unchanged |
| 2d. Planner safety (C3.6–C3.8, C2.7) | Warn before closing with unsaved input; remember the last tab; show the real last-saved time; focus the stage from "Planned completion missing" | `plan-edit.js`, `_EditPlanForm.cshtml`, `PlanReadService.cs` | The last-saved time may need a stored timestamp. **Check first**; drop the item if it would need a migration | Small–Medium | Low |
| 2e. Confirmation on plan withdraw | Match the actual-date withdraw | `_ProjectLifecycleWorkspace.cshtml:102-107` | No | Small | Low |

### Increment 3: Same words, same patterns (P1 + P2)

| Item | Current → proposed | Screens/components | Backend? | Size | Risk |
| --- | --- | --- | --- | --- | --- |
| 3a. Apply the vocabulary (E.5) | Labels, banners, hints and messages; names instead of codes; status labels instead of enum values | All Timeline partials, `stages.js`, `ActualProposalMessages.cs`, `ProjectOfficerWorkspaceService.cs` (stage titles) | Message strings only | Small–Medium (wide but shallow) | Low. Tests that assert on exact strings must be updated deliberately |
| 3b. Align backfill with the editor (Decision G2) | Same wording and badges, and the optional reason field | `_BackfillForm.cshtml`, `_BackfillModal.cshtml`, `overview.js`, `BackfillApply.cshtml.cs` (pass the note through) | Small: the service already accepts a note | Small | Low |
| 3c. Stage modal copy (C2.4) | Request-style option labels and one clear statement about approval | `_StageRequestModal.cshtml`, `stages.js:967-991` | No | Small | Low. Labels only; the transition values stay the same |
| 3d. Accessibility pass (C5) | Labels and ids, `aria-invalid`, list semantics, heading levels, text equivalents for colour and tooltips | Planner rows, stage modal, timeline partial | No | Small–Medium | Low |
| 3e. Small corrections | The wrong tooltip, the "Open timeline" panel switch, separator placement, the hint naming. Also: one colour for "completion date required" (C6.2); labelled, aligned date and duration on collapsed cards in chronological order (C6.4); the name first on every card, with a single missing-PDC signal (C6.5); "Plan approved on …" (C6.6) | As cited in C2.5, C2.6 and C6 | No | Small | Low |

### Increment 4: Close the loop (needs decisions G1 and G3)

| Item | Current → proposed | Backend? | Size | Risk |
| --- | --- | --- | --- | --- |
| 4a. Notifications for returned (and optionally approved) items in all three categories, including the reviewer's note | New notification kinds, raised by the existing decision services after commit | Yes (notification service; check whether `NotificationKind` needs a migration) | Medium | Medium: post-commit delivery must tolerate retries and duplicates (AGENTS.md) |
| 4b. Workspace "Returned" covers actual-date corrections, shows the project name and reason, and filters per stage rather than per project | `ProjectOfficerWorkspaceService.cs:1019-1096` | Read model | Small–Medium | Low |
| 4c. Withdraw for a pending stage request | A new service operation mirroring the plan and actual-date withdraw | Yes (a new status transition on the existing request; check the status column) | Medium | Medium: a business-rule addition, so it needs a decision |

**Suggested order:** 1, then 2, then 3. Increment 3 can run alongside 2 because they touch different areas, except 3a in the editor partials. Increment 4 follows the decisions below.

---

## G. Decisions for the product owner

| # | Decision | Options | Preferred and why |
| --- | --- | --- | --- |
| G1 | Should decisions notify the PO? (K-8 is open and optional) | (a) No notifications; the status summary only. (b) Notify on **returned** items only, with the note. (c) Notify on returned and approved | **(b).** A return needs action; an approval is visible in the summary and the dates. It keeps the notification volume low. |
| G2 | Backfill vs the actual-date editor | (a) Keep both, consistent behaviour. (b) Merge into one editor. (c) Keep the modal as a quick action over one shared model | **(a) now**, moving towards (c) if Phase E proceeds. Backfill is a useful, frequent shortcut, and a merge would add risk for little user gain. |
| G3 | Add withdraw for stage requests? | (a) Yes, like plan and actual dates. (b) No; editing replaces the request | **(a).** It makes the three workflows behave the same and gives the PO a way back. It is a business-rule addition, which is why it is listed here. |
| G4 | Where can a PO see their submissions across projects? | (a) Per project only (summary E.1). (b) Also fix the Workspace "Returned" list (4b). (c) A new "My submissions" page | **(a) + (b).** A new page adds navigation for a need the Workspace can meet. |
| G5 | Relationship to rule 27 (a single **Update**) | (a) Proceed incrementally as above and decide Phase E separately. (b) Pause UX work until Phase E | **(a).** Every increment survives a later move to a Change Set. |

---

## H. Explicit exclusions (not now)

- **Timeline Change Set and a single "Update" editor (rule 27, Phase E).** This needs a cross-category revision model; it is out of scope for UX fixes.
- **A new "My requests" dashboard or new navigation layers.** The Overview summary and the Workspace cover the need.
- **Merging backfill into the actual-date editor.** See G2.
- **Client-side plan calculation, or a live preview for Calculate.** This would duplicate business calculation in JavaScript, which AGENTS.md forbids. Clearer labels ("Calculate and save draft") are enough.
- **Autosave for Exact mode.** An unsaved-changes warning (2d) covers the risk more cheaply.
- **Real-time updates (SignalR) for approval status.**
- **A frontend framework, new tables or a workflow engine.**
- **HoD review-screen redesign (roadmap phase G).** It is not part of the PO journey. The only exception is shared wording, which 3a covers.
- **Showing draft or pending planned dates on the timeline itself.** This is attractive, but it needs careful design of the official vs proposed display (rule 23). Revisit it after Increment 1 if users still ask.

---

## Validation still required

| Item | How |
| --- | --- |
| ~~Rendered Timeline for a PO and at narrow width~~ | **Done** with dummy data (C7) |
| Why the Remarks load failed in the owner's instance (C6.8) | The server log for the remarks request at that time |
| ~~C1.6 stale "Returned for updates" banner~~ | **Confirmed** in the walkthrough (C7) |
| C3.7 Durations tab default for Exact users | Walkthrough |
| Whether POs actually miss outcomes (C1.x) and confuse "Lifecycle update pending" with being blocked | Short sessions with two or three real POs |
| Screen-reader behaviour (C5) | NVDA with keyboard-only navigation, on the planner and the stage modal |

The dummy project (Overview id 214) and the test accounts remain in the development database for later checks.

---

## Sources inspected

**Pages and partials**
- `Pages/Projects/Overview.cshtml(.cs)`, `Overview.Presentation.cs`, `Overview.Content.cs`
- `_ProjectLifecycleWorkspace.cshtml`, `_ProjectLifecyclePosition.cshtml`, `_ProjectLifecycleActions.cshtml`
- `_ProjectOperationalState.cshtml`, `_ProjectPendingActions.cshtml`, `_ProjectCommandHeader.cshtml`, `_ProjectRecordCompleteness.cshtml`, `_ProjectWorkspaceHosts.cshtml`
- `_BackfillForm.cshtml`, `_BackfillModal.cshtml`, `_PlanDiscardDraftModal.cshtml`, `_StageDirectApplyModal.cshtml`
- `Pages/Shared/_ProjectTimeline.cshtml`
- `Pages/Projects/Timeline/*`: EditPlan, EditActuals, Review, Historical and their partials, plus `ActualProposalMessages.cs`
- `Pages/Projects/Stages/*`: RequestChange, ApplyChange, DecideChange, BackfillApply, `_StageRequestModal.cshtml`
- `Pages/Approvals/Pending/*`

**View models and features**
- `ViewModels/ProjectPortfolioPresentationVm.cs`, `ViewModels/ActualsEditorVm.cs`
- `Features/Backfill/*`

**Scripts and styles**
- `wwwroot/js/projects/overview.js`, `actuals-edit.js`, `plan-edit.js`, `stages.js`
- `wwwroot/js/pages/project-portfolio.js`, `wwwroot/js/plan.js`
- `wwwroot/css/pages/project-portfolio.css`, `timeline.css`

**Services**
- `Services/Stages/PlanReadService.cs`, `ActualCorrectionProposalService.cs`, `ActualWriteAuthority.cs`
- `StageRequestService.cs`, `StageDecisionService.cs`, `StageNotificationService.cs`
- `Services/Plans/PlanApprovalService.cs`, `PlanDraftService.cs`, `PlanNotificationService.cs`, `PlanRevisionConsistency.cs`
- `Services/Projects/ProjectTimelineReadService.cs`
- `ProjectOfficerWorkspaceService.cs`
- `Models/Notifications/NotificationKind.cs`

**Tests**
- `ProjectManagement.Tests/Acceptance/ActualCorrectionEndToEndTests.cs`
- `ActualCorrectionProposal*Tests.cs`
- `PlanReadServiceIsolationTests.cs`, `EditPlanDurationSafetyTests.cs`

**Docs** (read after the independent pass): `AGENTS.md`, `docs/timeline.md` rules 9 and 22–27, `docs/timeline-proposal-roadmap.md`, `docs/phase-d-release-readiness.md` §§9–10.
