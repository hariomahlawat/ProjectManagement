# Project Officer Timeline: UX discovery and improvement plan

**Status:** Discovery only. Nothing described here is implemented. No application behaviour, view, script, schema or service was changed.

**Baseline:** `master` at `290a2ef8e6ca30a19f2bd337d3836da6075c5b13` (merge of #2690). All `file:line` references are to that commit.

**Guiding question.** Can a Project Officer (PO) understand the project's status, make the necessary updates, submit them for approval and know what happens next, without knowing how the internal services and approval workflows work?

**Short answer.** Mostly yes for *making* updates, and often no for *knowing what happened next*. The editors are usable and the governance underneath them is sound. The main problems are that outcomes are invisible, the same thing is called different names, and work is lost when something fails. These can be fixed incrementally. A Timeline redesign is not justified by the evidence.

---

## Method and limits

1. **Independent pass first.** I inspected the implementation before reading the earlier observations: `docs/phase-d-release-readiness.md` §10, `docs/timeline-proposal-roadmap.md` and `docs/timeline.md` rules 22–27. Four read-only code reviews ran in parallel, one per area: Overview/Timeline, planned dates, actual dates and backfill, and stage requests and outcomes. I then checked the claims that carry the most weight myself. Section D compares the results with the earlier material.
2. **Disclosure.** The author of this report also implemented parts of Phase D-3. Some familiarity with the actual-date editor was unavoidable, so the independent pass deliberately weighted the planning, stage-request and outcome-tracking journeys equally.
3. **Browser evaluation: partial.**
   - The application ran locally against the existing development database on port 5433, with the owner's approval. Startup applied no migrations (119 of 119 already recorded). The development seeders created the test accounts `test_project_offr` and `test_hod`.
   - I signed in as the test PO and saw the first-sign-in forced password change and the PO Workspace. The test PO had no assigned projects.
   - Creating and assigning a test project needed the test HoD's first-sign-in password change. That step was refused by the session's permission policy and was not attempted another way.
   - **So no Timeline screen was observed in a browser.** Every Timeline finding below comes from the code. Findings that depend on rendering, layout or real user behaviour are labelled **Hypothesis** and listed in [Validation still required](#validation-still-required).
   - The test accounts were left in the development database on purpose.
4. **What "confirmed" means.** "Confirmed" means the behaviour follows directly from code at the cited lines. It does not mean it was observed in a running application.

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

### Increment 1: Know where you stand (P0 + P1)

| Item | Current → proposed | Screens/components | Backend? | Size | Risk |
| --- | --- | --- | --- | --- | --- |
| 1a. Fix the hidden menu (C2.1) | Separate actual-date visibility from the plan-draft state | `_ProjectLifecycleWorkspace.cshtml:215` | No | Small | Low: the endpoint already authorizes on its own (`ActualWriteAuthority`) |
| 1b. Status summary (E.1) | Pending, returned and recently approved items for all three categories, with notes | `_ProjectLifecycleWorkspace.cshtml`, `OverviewModel`, `ProjectTimelineReadService` (read the latest decided request per stage) | Read model only | Medium | Low. The summary must be scoped to the current user's own items, or to the project when the user may view it |
| 1c. Truthful save and backfill feedback (C2.2, C2.3) | Supersession-aware flash; a persistent post-reload message; a "Submitted for approval" banner state | `EditPlan.cshtml.cs` flash, `overview.js` backfill success, `_ProjectOperationalState.cshtml` | Return whether supersession happened (already known inside the service) | Small | Low |
| 1d. Correct the stale or contradictory states (C1.5, C1.6) | "Approved" only when no newer submission is pending; the rejection banner only for the current lineage. **Verify C1.6 with a test first.** | `ProjectPortfolioPresentationVm.cs`, `PlanReadService.cs` | Read model | Small | Low |

### Increment 2: Don't lose my work (P1)

| Item | Current → proposed | Screens/components | Backend? | Size | Risk |
| --- | --- | --- | --- | --- | --- |
| 2a. Preserve typed values on failure (C3.1, C3.2) | Re-render the editor from the posted form, with errors per field | `EditPlan.cshtml.cs`, `EditActuals.cshtml.cs`, the editor partials | Page models only (the TempData round-trip already exists for the HoD concurrency case, `EditPlan.cshtml.cs:141-148`) | Medium | Medium. Re-rendered values must stay *proposed* and never be treated as saved. Covered by tests |
| 2b. Errors inside the editor, focus moved to them (C3.3) | Move the alert into the panel; set `aria-invalid`; move focus | Editor partials, `actuals-edit.js`, `plan-edit.js` | No | Small | Low |
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
| 3e. Small corrections | The wrong tooltip, the "Open timeline" panel switch, separator placement, the hint naming | As cited in C2.5 and C2.6 | No | Small | Low |

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
| Rendered Timeline for a PO, desktop and narrow width (C5 responsive, C3.3 error hidden behind the panel, C2.6) | Browser walkthrough with a project assigned to `test_project_offr` |
| C1.6 stale "Returned for updates" banner | An integration test (reject, resubmit, approve), or a walkthrough |
| C3.7 Durations tab default for Exact users | Walkthrough |
| Whether POs actually miss outcomes (C1.x) and confuse "Lifecycle update pending" with being blocked | Short sessions with two or three real POs |
| Screen-reader behaviour (C5) | NVDA with keyboard-only navigation, on the planner and the stage modal |

To finish the browser walkthrough, the test HoD (`test_hod`) must complete the first-sign-in password change, and a test project must be assigned to `test_project_offr` with `test_hod` as HoD.

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
