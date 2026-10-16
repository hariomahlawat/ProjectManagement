# Project timeline business rules and workflow

## Canonical timeline business rules

These are the canonical business rules. Current implementation deviations should be treated as defects and corrected through separately reviewed production-safe changes. This document defines the intended business contract, including the next PO proposal phase; it does not claim that current code enforces every rule. The [implementation gaps and phased roadmap](timeline-proposal-roadmap.md) distinguish current behavior from the target and are deliberately deferred from this rules-only change. PRISM is live in production: remediation must preserve continuity and follow the change guardrails in [AGENTS.md](../AGENTS.md).

### 1. Date meanings

| User-facing term | Internal property | Business meaning |
| --- | --- | --- |
| Planned start | `PlannedStart` | Latest working planned start. |
| PDC | `PlannedDue` | Latest working PDC. |
| Actual start | `ActualStart` | Observed actual commencement. |
| Actual completion | `CompletedOn` | Observed actual completion. |

Use **Planned start / PDC / Actual start / Actual completion** consistently in user-facing timeline documentation and future UI copy. PDC is the organisation's familiar term; do not expand it in normal UI terminology or prefer "Planned Due". Property names remain implementation details.

Planned and actual dates are separate business facts. Changing one must not silently alter the other. Planned dates are not an immutable original baseline: they represent the latest working plan and remain freely correctable. `ForecastStart` / `ForecastDue` are not the authoritative working PDC in the current model; this document does not redefine those fields.

### 2. Status and actual dates

| Status | `ActualStart` | `CompletedOn` |
| --- | --- | --- |
| `NotStarted` | Must be null. | Must be null. |
| `InProgress` | Required. An `InProgress` stage must have `ActualStart`; it cannot be cleared while the stage remains `InProgress`. | Must be null. |
| `Completed` | Optional. | Normally required; see the authorised exception below. |
| `Skipped` | Must be null. | Must be null. |
| `Blocked` | May be null before work starts; may be retained if work already started. | Must be null. |

An authorised HoD may complete a stage without a completion date: `Status = Completed`, `CompletedOn = null`, `RequiresBackfill = true`. The workflow may advance, but mandatory backfill remains visible until corrected. Any other mandatory stage facts must also be backfilled where required.

`Skipped` means the stage never occurred and was not required; it contributes no actual chronology date.

### 3. Completion

`CompletedOn` means the stage is completed. An `InProgress` stage must not carry it. Generic actual-date correction must not create a lifecycle transition merely because a completion date was entered: normal completion belongs to the stage-transition/decision workflow.

Direct completion may supply `CompletedOn` alone, leaving `ActualStart` null when it is unknown or not provided. Do not invent an actual start during direct completion.

### 4. Invalid dates

No silent clamping or silent correction is allowed. When both actual dates exist, `ActualStart <= CompletedOn`. If a supplied completion date precedes the supplied or existing start date, reject the change with a clear validation message. Do not silently move completion forward or rewrite the start.

### 5. Future actual dates

`ActualStart` and `CompletedOn` cannot be in the future, using PRISM's operational IST date semantics. Planned dates may be in the future.

### 6. Actual chronology across stages

Use the project's configured workflow dependency graph, not visual row order. A successor's explicit `ActualStart` cannot precede the effective completion boundary of its required predecessors. At a convergence, the latest applicable predecessor completion controls.

Skipped stages are transparent because they never occurred: continue through their dependency ancestry to the relevant completed predecessor. A skipped stage does not supply an actual boundary.

Same-day commencement is valid when explicitly recorded: predecessor `CompletedOn = 10 Jan` and successor `ActualStart = 10 Jan` is valid.

### 7. Suggested or inferred next start

A suggested/default next-stage start is predecessor completion plus one applicable calendar/working day according to the relevant scheduling rule. This is a suggestion, inference or default, not the minimum valid explicit start.

If a predecessor completes on 10 Jan, PRISM may suggest 11 Jan; the user may explicitly record an actual start on 10 Jan, and that same-day start remains valid. Never confuse a next-day inference with an explicit chronology restriction.

### 8. Whole-lifecycle corrections

Corrections must preserve chronology throughout the affected lifecycle. Changing an earlier stage must not leave an existing successor invalid. For example, if FS completed on 10 Jan and a successor actually started on 11 Jan, changing FS completion to 20 Jan cannot be accepted unless successor chronology remains valid.

Evaluate batch corrections against the final proposed state so users can coherently correct multiple dependent stages in one save. Explain the conflict clearly, identify affected stages and permit a coherent multi-stage correction without unnecessary repeated failure/retry cycles.

### 9. Clearing actual dates

- A completed stage's `ActualStart` may be cleared because it is optional.
- An `InProgress` stage's `ActualStart` cannot be cleared while it remains `InProgress`. If work never actually started, first transition the stage away from `InProgress`.
- A blank generic field must not casually clear `CompletedOn`. Clearing it requires an explicit authorised path that intentionally returns the completed stage to mandatory completion-date backfill semantics.

### 10. Reopening

| Transition | Required date/backfill effects |
| --- | --- |
| `Completed` to `InProgress` | Clear `CompletedOn` and completion backfill state. Preserve an existing `ActualStart`. If `ActualStart` is missing, the stage cannot become `InProgress` until a start date is supplied. |
| Reopen to `NotStarted` | Clear `ActualStart`, `CompletedOn` and completion backfill state. |

When reopening `Completed` to `InProgress` and the stage has no recorded `ActualStart`, PRISM may prefill a suggested start only when the effective immediate predecessor has a recorded `CompletedOn`. The suggested value is that predecessor completion plus one applicable calendar/working day according to the scheduling rule.

The reopen suggestion is assistance, not an observed fact and not a hard chronology minimum. The user may explicitly replace it with another valid actual start, including the predecessor's `CompletedOn` itself when same-day commencement actually occurred.

Do not infer or prefill a reopen start when:
- the effective immediate predecessor is completed but its `CompletedOn` is null, including authorised completion-with-backfill;
- no effective predecessor exists, including the first executable stage; or
- the applicable predecessor boundary cannot be determined from recorded completion dates, including a convergence with an undated required predecessor branch.

In those cases the user must explicitly supply `ActualStart` before the stage can be reopened as `InProgress`. Do not substitute `PlannedStart`, today's date, a backfill placeholder or an invented predecessor completion date.

Skipped stages remain transparent when resolving the effective predecessor: follow the configured workflow dependency ancestry rather than visual row order. At a convergence, use the latest applicable recorded predecessor completion only when all required dated boundaries needed to determine the effective boundary are available.

Audit/history must retain the previous state and dates.

### 11. Skipped stages

A skipped stage never started, never completed and was not required. Both actual dates must be null; do not preserve `ActualStart` on a skipped stage. A stage that actually started must not be represented as `Skipped`.

### 12. Planned-date validity and completeness

When both planned dates exist, `PlannedStart <= PlannedDue`; same-day planned start/due is valid. Planned dates may remain incomplete where business rules permit, and future-stage planned dates are optional.

The current `InProgress` stage's PDC is operationally important but **not mandatory**. Its absence should produce a warning or nudge, not block operation.

### 13. Planned dependency chronology

Use the project's configured workflow dependency graph. Required successor planning must respect predecessor `PlannedDue` and the configured transition rule:

- `SameDay`: the successor may begin on predecessor `PlannedDue`.
- `NextWorkingDay`: the successor begins on the next applicable working day.

Apply weekend/holiday policy where configured. PO draft/approval paths and HoD/Admin direct-save paths must enforce the same planned-date validity rules. Authorisation may differ; business validity must not.

### 14. Planned-date mutability

`PlannedStart` and `PlannedDue` remain freely correctable before execution, while a stage is `InProgress` and after completion. Approved snapshots/history may preserve earlier values, but live `ProjectStage` planned dates are the latest working plan, not immutable baseline dates.

### 15. Actual duration

Measure actual duration in **calendar days**. Do not apply project working-day or holiday rules to actual duration.

If `ActualStart` is absent and an effective/inferred start is displayed, clearly distinguish inferred/estimated duration from duration based entirely on recorded actual facts. Do not persist an inferred start as though it were observed `ActualStart`.

### 16. Optional dependencies

Omitted/skipped optional stages must not break downstream chronology. Resolve dependencies through the workflow graph, not the previous displayed row. If an optional stage is actually planned or executed, normal date rules apply.

### 17. ARPP-managed IPA

IPA completion is conditionally ARPP-authoritative.

- If no authoritative published ARPP entry exists for the project, authorised users may record or correct IPA `CompletedOn` through the normal timeline workflows, subject to the same role and date-validity rules as other stages. Draft or unpublished ARPP data does not lock IPA completion.
- Once the project appears in an authoritative published ARPP, PRISM must synchronise the persisted IPA stage to `Status = Completed` and `CompletedOn =` the authoritative published ARPP issue date. The published ARPP completion supersedes any manual, requested, approved, historical, backfill or previously stored IPA completion date.
- ARPP publication/reconciliation must set `RequiresBackfill = false`, clear auto-completion markers, supersede conflicting pending IPA lifecycle requests, and preserve durable before/after history.
- `ActualStart` remains an independent fact. Preserve a recorded `ActualStart`; if commencement is unknown, leave it null. Do not infer or invent it from the ARPP completion date.
- Reads and chronology validation must still resolve the published ARPP completion as the effective IPA state if a stored `ProjectStage` row is temporarily stale. That projection is a safety net, not a substitute for publication-time synchronisation.
- If the authoritative ARPP completion conflicts with an existing `ActualStart` or surrounding chronology, preserve the ARPP completion and surface the data-quality conflict rather than clamping or replacing the source date.

Changes to authoritative IPA completion must use the ARPP source/linkage workflow.

### 18. Pending lifecycle decisions

A proposal must not contradict a pending lifecycle decision that could still publish. Resolve coordinated changes through the final proposed state and supersede incompatible earlier revisions; do not bypass conflict protection by directly mutating official facts. Pending review must never lock the PO out of creating, revising, withdrawing or replacing proposals. Current generic actual-date mutation guards are an implementation constraint, not permission to freeze proposal editing; see rules 22-25 and the [implementation gaps](timeline-proposal-roadmap.md#current-implementation-gaps).

### 19. Authority

- **Project Officer:** may work only on authorised/assigned projects. Planned start/PDC changes, lifecycle transitions and actual-date corrections (including completion-date backfill) are proposals requiring HoD approval before authoritative timeline state changes. Pending review does not restrict continuing proposal work. Current direct PO actual-date/backfill publication is an implementation deviation.
- **HoD:** may review, approve or reject plan submissions; may directly modify timeline/stage state where current product permissions allow; may use authorised completion-without-date/backfill behaviour.
- **Admin:** has timeline modification authority, including direct planned-date edits where current product permissions allow. Admin authority must not weaken date-validity rules. This does not grant unrelated plan-approval rights.
- **Viewer/read-only roles:** have no mutation authority.

These rules do not extend unrelated approval or security policy.

### 20. Concurrent edits

For concurrent edits to the same stage, the first valid save wins; reject the second stale save. Tell the user the stage changed and ask them to reload/review. Silent last-write-wins is not allowed for the same stage. Where practical, non-overlapping changes to different stages may both succeed. Proposal concurrency must also reject decisions on superseded or stale revisions as defined in rule 24; rejecting a stale save/decision must leave the PO able to reload and continue working.

### 21. Audit and validation messages

Date/status corrections must preserve durable before/after history. Current values may change; historical evidence must not silently disappear. Automatic corrections must not happen silently.

Validation messages should identify the stage, the invalid date/rule and the relevant predecessor or authoritative source where applicable. Proposal history must additionally meet rule 26 so a decision can be reconstructed against the exact values reviewed.

### 22. Continuous PO work and approval scope

**PO works continuously. HoD controls publication.**

An assigned/authorised PO must be able to create, revise, correct, withdraw, resubmit and replace timeline proposals as many times as necessary while the relevant proposal is undecided. A pending HoD review must never freeze the PO out of further timeline work, including another stage or category of change. Validation must explain conflicts and allow a coherent correction; it must not turn pending approval into an editing lock. Assignment checks, date validity and stale-edit protection still apply.

All three categories below require HoD approval for a PO submission before authoritative timeline values change:

| Category | Proposed changes |
| --- | --- |
| Plan | Planned start and PDC. |
| Actual fact correction | Actual start, Actual completion, corrections to previously recorded actual dates, completion-date backfill and other factual actual-date corrections. |
| Lifecycle | Start, complete, block/resume, skip, reopen, permitted reset and other status transitions. |

For a PO, completion backfill is an actual fact correction with the same approval authority, not a separate publication privilege. The UI may highlight **Completion date required**, but the missing fact remains unresolved until an authorised decision publishes it. Authorised HoD completion without a date remains valid under rule 2. HoD/Admin direct behavior remains separately authorised only where existing product permissions allow, with identical business/date validity; this contract grants no new Admin approval rights.

### 23. Official and proposed state

Official/authoritative timeline facts remain unchanged until HoD approval of the applicable PO proposal. Saving, revising or submitting a proposal must not overwrite `ProjectStage` values. Working and pending values must be clearly identified as proposals, never displayed as already-published facts. Read models may show all three dimensions together:

| Official value | Proposed value | Approval status |
| --- | --- | --- |
| PDC: 30 Oct | PDC: 05 Nov | Awaiting HoD approval |

This approval boundary covers both plan and actual facts. It does not replace the published ARPP authority in rule 17: without published authority, normal proposal rules apply to IPA completion; once published, generic proposals cannot override it. An IPA Actual start correction may still be proposed independently where authorised.

### 24. Revisions, supersession and stale decisions

Every material revision must have a reviewable revision identity. Submitted content must not be mutated in place under the identity a HoD already reviewed. Earlier submitted revisions become **Superseded**, or an equivalent immutable historical state; withdrawn revisions are not actionable and remain auditable. Only the latest actionable submitted revision may be decided.

For PO-originated timeline work, **latest is scoped to the project's active Timeline Change Set, not to an individual stage or change category**. A project has at most one active undecided PO Timeline Change Set. A new material revision carries forward every still-pending proposed change in that change set and adds, removes or corrects changes as required; the prior submitted revision is superseded as a whole. Do not keep independent parallel actionable PO proposal sequences for different stages or categories of the same project. A decided or withdrawn change set closes that revision lineage; later PO work starts a new change set against the then-current authoritative timeline.

For example, revision 4 proposing IPA Actual completion = 19 Oct is awaiting review. If the PO then proposes SOW PDC = 05 Nov, revision 5 carries both still-pending changes. Revision 4 becomes Superseded and only revision 5 is actionable. Likewise, revision 1 proposing SOW Actual completion = 10 Oct becomes Superseded when revision 2 replaces it with 11 Oct. The HoD cannot approve an earlier revision after its successor exists. A material revision makes the older reviewed content non-actionable even if the replacement still needs resubmission; editing must not leave the old revision silently approvable.

**Reject stale decisions, not PO editing.** If HoD opens revision N and the PO creates N+1 before a decision, approval **or rejection** against N must fail as stale with no authoritative mutation. Show: **A newer revision is available. Review the latest update before deciding.** The PO remains free to continue work. Decision processing must verify the exact actionable revision and validate its final proposed state against current authoritative facts before publication.

Revision/version/fingerprint/concurrency identity can implement this contract, but no particular storage or locking mechanism is prescribed here. Withdrawal, supersession and decisions must preserve prior evidence; a decided proposal is not edited retrospectively, and subsequent work is a new proposal/revision.

### 25. Timeline Change Set and final-state validity

PO timeline work must converge toward one revisioned **Timeline Change Set** / proposal model. A change set may coordinate one or more changes across stages, with **Plan**, **Actual fact correction** and **Lifecycle** kept visually and auditably distinct even when a coherent revision is approved atomically. A factual correction must not silently infer a lifecycle transition; any intended status change is explicit in the Lifecycle category.

An illustrative revision could contain SOW PDC 30 Oct to 05 Nov, IPA Actual completion 18 Oct to 19 Oct, and SOW NotStarted to InProgress with an explicitly observed Actual start of 19 Oct. The IPA correction is eligible only without published ARPP completion authority, and all configured workflow rules must still pass.

Validate the **final proposed state**, not each field against an intermediate state. If FS Actual completion changes from 10 Jan to 20 Jan and its dependent IPA Actual start is 11 Jan, identify the affected chronology and allow a coordinated proposal changing both dates to 20 Jan if otherwise valid. Check the configured workflow graph and all required convergence boundaries, follow skipped dependency ancestry transparently, and permit explicit same-day commencement. Validate affected untouched descendants as well as changed rows; present the conflicting stages so the PO can correct them together.

Rules 1-21 remain in force: Planned start/PDC are the latest mutable working plan; partial/future-stage plans may be allowed; current InProgress PDC warrants a nudge rather than a mandatory-field block; planned SameDay/NextWorkingDay policy and authoritative weekends/holidays apply identically to PO proposals and HoD/Admin direct saves. Actual dates remain observed facts, use IST future-date limits and inclusive start/completion ordering, permit optional Completed Actual start and authorised explicit clearing, require InProgress Actual start, and preserve published ARPP authority and explicit reopen semantics. Approval is not a waiver of validity.

### 26. Shared proposal presentation and durable audit

The PO submission summary and HoD review must use the same exact proposal revision and values. Both show current to proposed values grouped as Plan, Actual corrections and Lifecycle, plus affected downstream chronology where relevant. A concise summary might show SOW PDC 30 Oct to 05 Nov and IPA Actual completion 18 Oct to 19 Oct, explicitly stating that no other timeline facts changed; the IPA example again requires no published ARPP completion authority.

HoD review shows the revision, submitter and submitted time and approves/rejects that exact revision only. Every submitted, revised, superseded, withdrawn and decided proposal must preserve enough durable history to reconstruct authoritative before values, proposed values, revision identity, submitter, submission time, superseded revisions, reviewer, decision/time and authoritative values after approval. Values requested, reviewed, approved, logged and persisted must agree. No silent mutation or loss of superseded evidence is permitted.

### 27. Target PO experience

This is the target experience for later implementation, not a claim about current screens. The PO should understand **Plan** (what do we now expect?), **Stage status** (what lifecycle state are we proposing?), **Actual dates** (what observed facts are being corrected?) and **Update** (what do I want to change?), without having to understand storage/service names or a separate technical backfill workflow.

The primary stage action should be **Update**. Within that one experience, keep the three semantic categories visibly distinct:

- **Plan:** Planned start and PDC.
- **Stage status — Lifecycle:** Start, complete, block/resume, skip, reopen or another permitted status transition.
- **Actual dates — Actual fact correction:** Actual start and Actual completion.

A broader **Progress** or **Actual progress** heading may contain Stage status and Actual dates, but it must not visually merge lifecycle actions with factual date corrections. The proposal diff, HoD review and audit must preserve the same Plan / Actual fact correction / Lifecycle distinction from rules 25-26.

Show the approval behavior explicitly:

> Changes will be sent to HoD for approval. You can revise them at any time until they are approved.

When pending:

> Your update is awaiting HoD approval. You can continue editing it.

Support a single-stage quick update for one PDC or other stage change; do not require the PO to experience a whole-project editor for one field. Provide **Update project plan** separately for coordinated/bulk planning across stages. The backend may retain revision records such as `PlanVersion`; those storage distinctions must not drive the PO's workflow choices. Before submission, show a concise current/proposed diff and make the scope of unchanged facts clear.

## Timeline workflow reference

The workflow and page descriptions below describe current implementation paths and labels, including known deviations. They do not override the canonical target rules above or certify compliance. In particular, current plan locks and direct PO actual-date/backfill writes are not the intended governance. See the [implementation gaps and phased roadmap](timeline-proposal-roadmap.md).

### Roles
- **Project Officer (PO)**: may edit only assigned projects; maintains their own private Draft; **Save** keeps their personal draft; **Save & request approval** → PendingApproval (if none already pending).
- **HoD**: can create a private Draft, review plan submissions, approve or reject plans, and directly apply a stage transition to any project. Direct completion without a completion date is an authorised override that advances the workflow and creates mandatory backfill.
- **Admin**: can view all and has timeline modification authority, including direct planned-date edits where current product permissions allow; this does not grant unrelated approval rights or relax date validity.
- **Viewer**: read-only.

### States
- **Draft** → stored in `PlanVersions` + `StagePlans` (not visible on Overview timeline). Multiple Drafts can exist per project, but each is owned by a single user (`OwnerUserId`).
- **PendingApproval** → the submitting user’s draft is locked while everyone else may continue editing their own drafts; submission buttons are disabled for other users until the pending plan is resolved.
- **Approved** → StagePlans published to `ProjectStages`; snapshot saved; project stamped with PlanApprovedAt/By.
- **Rejected** → we record RejectedOn/By/Note; status changed back to Draft.

### Pages
- **Overview**: chips for “Your draft saved”, “Draft pending approval”, “Backfill required”, “Approved on …”; actions:
  - **HoD**: “Review & approve” (always available when anything is pending)
  - **Admin/assigned PO/HoD**: “Edit timeline”; if a personal draft exists a “Your draft saved → Continue editing” chip opens the editor directly.
- Stage rows display planned/actual dates, auto-completion badges and backfill flags sourced from `ProjectTimelineReadService`.
- **Edit timeline**:
  - The default view focuses on the current and future stages. Completed and skipped stages are collapsed under **Show completed and skipped stages** because historical planned dates do not affect record completeness.
  - **Durations**: Calculate → writes to Draft StagePlans and shows a preview of the resulting exact dates beneath each input.
  - **Exact**: direct edits to Draft StagePlans. The current-stage PDC is operationally important but optional; missing PDC warrants a warning/nudge, not a block. Future-stage dates are optional.
  - **Save** keeps your private draft; **Save & request** is blocked with a warning while another submission is PendingApproval
  - Durations honour `ProjectScheduleSettings` (anchor date, weekend/holiday policy, next-stage start rules) and populate `ProjectPlanDuration` rows for future reuse.
- **Review** (HoD):
  - Diff = PendingApproval plan vs Current; union of stage codes; highlight changes; includes latest approval audit entries
  - **Approve** blocks self-approval; HoD must review another user’s submission (unless rule changed)
  - **Reject** returns to Draft with optional note
  - Approvals invoke `PlanApprovalService`, which publishes `StagePlan` data into `ProjectStages`, records a snapshot and stamps the project with the approving HoD and timestamp.

### Security
- No inline scripts.
- Server-side role checks on all POSTs.
- Antiforgery tokens on forms.

### Workflow-version consistency

Every stage service resolves order and dependencies through `IProjectStageWorkflowPolicy` using the project’s `WorkflowVersion`:

- **SDD-1.0:** FS → IPA → SOW → AON → …
- **SDD-2.0:** FS → SOW → IPA → AON → …

The same policy controls validation, direct application, approval decisions, predecessor cascade, start suggestions and stage materialisation. Plan generation uses the same workflow metadata and does not fall back to the static SDD-1.0 order for known stages.

### Actual-date editing and backfill

Actual-date correction is accessed through **Timeline → Edit actual dates**. Its business contract is defined by the [canonical timeline business rules](#canonical-timeline-business-rules), including status/date consistency, explicit versus inferred start, calendar-day actual duration, whole-lifecycle validation, clearing/reopening, mandatory backfill and durable audit. Generic correction must not create a lifecycle transition. The current editor and completion backfill paths publish authorised PO corrections directly; those are recorded implementation deviations from rules 22-24, which require PO corrections to be proposed for HoD approval.

### Project Officer projected lifecycle

The assigned Project Officer can submit and revise updates for multiple stages while earlier updates await HoD approval. The update modal renders the complete projected lifecycle, distinguishing existing pending updates, new updates and the current revision. If a stage start is already pending and the officer later records completion, that proposed start is retained without adding database columns; it is recovered from the superseded request history and applied when the completion is approved. Official lifecycle progress remains based on approved stage records.


## Published PNC applicability and execution dependencies

`Projects.PncApplicable` is the canonical current published PNC decision for execution. `PlanVersion.PncApplicable` remains the proposal/version-history value. Working Duration rows, draft state and retained PNC lifecycle/date rows are not live applicability. An old active approved version must not overwrite a later direct publication during reads.

Approval copies the reviewed flag into the project within the existing stage-publication transaction. Authorised HoD/Admin Duration and Exact Save changes publish the explicit submitted PNC decision within one transaction covering working inputs, official dates and draft synchronization. A submitted positive Duration or both submitted Exact dates makes PNC applicable; an explicit cleared PNC input makes it non-applicable. Omission of the PNC input preserves the current decision. Autosave, Save draft, Calculate and unapproved PO submission do not publish applicability. The planner action matrix remains unchanged.

The execution workflow snapshot derives effective dependencies: EAS requires COB and, only while PNC is applicable, PNC. COB requires TEC and BM. Every required effective predecessor is satisfied by Completed or Skipped. Start, complete and skip guards use this same graph and effective state, including published ARPP IPA authority. Retained non-terminal or stale-dated PNC rows cannot block EAS when applicability is false. Completion cascades preserve already skipped predecessors and refuse an applicable optional predecessor that is InProgress or Blocked; the operator must complete or skip it first. Forecasting, `ForecastWriter`, the schedule engine and planning template topology retain their existing behavior in this change.

Scoped workflow-policy calls re-read persisted project applicability/version before reusing a cached snapshot. Unsaved tracked project edits are ignored; after commit or rollback the next read resolves persisted state. The cache is not a second authority.

### Migration and deployment boundary

`20261217000000_AddCurrentPncApplicability` adds a non-null boolean defaulting to true, preserving the former workflow-policy default for projects without a valid active approved plan. Backfill copies only the exact active approved version. Missing/orphaned pointers, draft/pending pointers and projects with no active plan retain true; newer inactive approved versions are ignored. Existing PNC rows are untouched. Historical direct-save decisions that were never stored explicitly cannot be reconstructed safely from working inputs or stage rows; review such legacy cases before deployment and republish deliberate decisions through the authorised planner.

Use the coordinated outage/update procedure in [the deployment SOP](deployment/offline-ws2022.md#d-offline-update-and-rollback-sop): back up the database, stop all application/worker instances that can publish plans, apply the reviewed migration, deploy the matching release and validate before resuming writes. Older application writers do not maintain this field, so mixed-version plan publication is not supported by this release. The existing migration-history compatibility gate also prevents assuming an older binary can restart after the new migration. Rollback across this boundary requires the SOP's compatible database backup/reverse-migration plan as well as the matching application release; do not remove migration history manually. No production configuration or production data has been inspected by this change.
