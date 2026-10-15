# Project timeline business rules and workflow

## Canonical timeline business rules

These are the canonical business rules. Current implementation deviations should be treated as defects and corrected through separately reviewed production-safe changes. This document defines the agreed rules before implementation debugging; it does not claim that current code enforces every rule. PRISM is live in production: remediation must preserve continuity and follow the change guardrails in [AGENTS.md](../AGENTS.md).

### 1. Date meanings

| Field | Business meaning |
| --- | --- |
| `PlannedStart` | Latest working planned start date. |
| `PlannedDue` | Latest working planned completion date / PDC. |
| `ActualStart` | Date work actually began. |
| `CompletedOn` | Date work actually completed. |

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

The current `InProgress` stage's `PlannedDue` / PDC is operationally important but **not mandatory**. Its absence should produce a warning or nudge, not block operation.

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

Where published ARPP controls IPA completion, ARPP is authoritative for `CompletedOn`; generic timeline editing must not override it. `ActualStart` may remain independently recordable/correctable where allowed. Changes to authoritative completion must use the ARPP source/linkage workflow.

### 18. Pending lifecycle decisions

Generic actual-date correction must not concurrently edit a stage in a way that contradicts its pending lifecycle decision. Existing conflict protection remains part of the business contract.

### 19. Authority

- **Project Officer:** may edit/propose plan data only for authorised/assigned projects. Normal plan publication and stage-change requests follow the existing approval workflows.
- **HoD:** may review, approve or reject plan submissions; may directly modify timeline/stage state where current product permissions allow; may use authorised completion-without-date/backfill behaviour.
- **Admin:** has timeline modification authority, including direct planned-date edits where current product permissions allow. Admin authority must not weaken date-validity rules. This does not grant unrelated plan-approval rights.
- **Viewer/read-only roles:** have no mutation authority.

These rules do not extend unrelated approval or security policy.

### 20. Concurrent edits

For concurrent edits to the same stage, the first valid save wins; reject the second stale save. Tell the user the stage changed and ask them to reload/review. Silent last-write-wins is not allowed for the same stage. Where practical, non-overlapping changes to different stages may both succeed.

### 21. Audit and validation messages

Date/status corrections must preserve durable before/after history. Current values may change; historical evidence must not silently disappear. Automatic corrections must not happen silently.

Validation messages should identify the stage, the invalid date/rule and the relevant predecessor or authoritative source where applicable.

## Timeline workflow reference

The workflow and page descriptions below provide context; they do not override the canonical rules above or certify complete implementation compliance.

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
  - **Exact**: direct edits to Draft StagePlans. The current-stage planned completion/PDC is operationally important but optional; missing PDC warrants a warning/nudge, not a block. Future-stage dates are optional.
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

The same policy controls validation, direct application, approval decisions, predecessor cascade, auto-start and stage materialisation. Plan generation uses the same workflow metadata and does not fall back to the static SDD-1.0 order for known stages.

### Actual-date editing and backfill

Actual-date correction is accessed through **Timeline → Edit actual dates**. Its business contract is defined by the [canonical timeline business rules](#canonical-timeline-business-rules), including status/date consistency, explicit versus inferred start, calendar-day actual duration, whole-lifecycle validation, clearing/reopening, mandatory backfill and durable audit. Generic correction must not create a lifecycle transition.

### Project Officer projected lifecycle

The assigned Project Officer can submit and revise updates for multiple stages while earlier updates await HoD approval. The update modal renders the complete projected lifecycle, distinguishing existing pending updates, new updates and the current revision. If a stage start is already pending and the officer later records completion, that proposed start is retained without adding database columns; it is recovered from the superseded request history and applied when the completion is approved. Official lifecycle progress remains based on approved stage records.
