# Timeline Pipeline — Current (Hardened)

## Roles
- **Project Officer (PO)**: may edit only assigned projects; maintains their own private Draft; **Save** keeps their personal draft; **Save & request approval** → PendingApproval (if none already pending).
- **HoD**: can create a private Draft, review plan submissions, approve or reject plans, and directly apply a stage transition to any project. Direct completion without a completion date is an authorised override that advances the workflow and creates mandatory backfill.
- **Admin**: read-only for approvals; can view all.
- **Viewer**: read-only.

## States
- **Draft** → stored in `PlanVersions` + `StagePlans` (not visible on Overview timeline). Multiple Drafts can exist per project, but each is owned by a single user (`OwnerUserId`).
- **PendingApproval** → the submitting user’s draft is locked while everyone else may continue editing their own drafts; submission buttons are disabled for other users until the pending plan is resolved.
- **Approved** → StagePlans published to `ProjectStages`; snapshot saved; project stamped with PlanApprovedAt/By.
- **Rejected** → we record RejectedOn/By/Note; status changed back to Draft.

## Pages
- **Overview**: chips for “Your draft saved”, “Draft pending approval”, “Backfill required”, “Approved on …”; actions:
  - **HoD**: “Review & approve” (always available when anything is pending)
  - **Admin/assigned PO/HoD**: “Edit timeline”; if a personal draft exists a “Your draft saved → Continue editing” chip opens the editor directly.
- Stage rows display planned/actual dates, auto-completion badges and backfill flags sourced from `ProjectTimelineReadService`.
- **Edit timeline**:
  - The default view focuses on the current and future stages. Completed and skipped stages are collapsed under **Show completed and skipped stages** because historical planned dates do not affect record completeness.
  - **Durations**: Calculate → writes to Draft StagePlans and shows a preview of the resulting exact dates beneath each input.
  - **Exact**: direct edits to Draft StagePlans. The current-stage planned completion is operationally important; future-stage dates are optional.
  - **Save** keeps your private draft; **Save & request** is blocked with a warning while another submission is PendingApproval
  - Durations honour `ProjectScheduleSettings` (anchor date, weekend/holiday policy, next-stage start rules) and populate `ProjectPlanDuration` rows for future reuse.
- **Review** (HoD):
  - Diff = PendingApproval plan vs Current; union of stage codes; highlight changes; includes latest approval audit entries
  - **Approve** blocks self-approval; HoD must review another user’s submission (unless rule changed)
  - **Reject** returns to Draft with optional note
  - Approvals invoke `PlanApprovalService`, which publishes `StagePlan` data into `ProjectStages`, records a snapshot and stamps the project with the approving HoD and timestamp.

## Security
- No inline scripts.
- Server-side role checks on all POSTs.
- Antiforgery tokens on forms.

## Workflow-version consistency

Every stage service resolves order and dependencies through `IProjectStageWorkflowPolicy` using the project’s `WorkflowVersion`:

- **SDD-1.0:** FS → IPA → SOW → AON → …
- **SDD-2.0:** FS → SOW → IPA → AON → …

The same policy controls validation, direct application, approval decisions, predecessor cascade, auto-start and stage materialisation. Plan generation uses the same workflow metadata and does not fall back to the static SDD-1.0 order for known stages.

## Actual-date and backfill rules

- **Completed stage:** completion date is authoritative. Actual start is optional; when absent, duration is inferred from the preceding applicable stage’s completion date plus one day.
- **Current in-progress stage:** actual start and planned completion are operationally important.
- **Future stage:** planned dates are optional.
- **Skipped stage:** dates are not required.
- **Authorised completion override:** any HoD may complete a stage without a completion date. The workflow advances, the stage counts as operationally completed, and mandatory backfill remains until the completion date and any mandatory stage facts are recorded.

Actual dates can be corrected directly from **Timeline → Edit actual dates**. The editor avoids artificial status transitions and preserves the stage audit trail through the actuals-update workflow.

## Project Officer projected lifecycle

The assigned Project Officer can submit and revise updates for multiple stages while earlier updates await HoD approval. The update modal renders the complete projected lifecycle, distinguishing existing pending updates, new updates and the current revision. If a stage start is already pending and the officer later records completion, that proposed start is retained without adding database columns; it is recovered from the superseded request history and applied when the completion is approved. Official lifecycle progress remains based on approved stage records.
