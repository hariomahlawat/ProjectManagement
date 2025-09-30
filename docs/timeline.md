# Timeline Pipeline — Current (Hardened)

## Roles
- **Project Officer (PO)**: may edit only assigned projects; maintains their own private Draft; **Save** keeps their personal draft; **Save & request approval** → PendingApproval (if none already pending).
- **HoD**: can also create a private Draft, review their own submissions and approve another user’s submission; **Approve** publishes to live + snapshots + stamps; **Reject** returns to Draft with note.
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
- **Edit timeline** (PO):
  - **Durations**: Calculate → writes to Draft StagePlans and shows a preview of the resulting exact dates beneath each input
  - **Exact**: direct edits to Draft StagePlans
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

## Correcting HoD Stage Start Dates

The stage transition services (`StageRequestService`, `StageDirectApplyService`, and
`StageProgressService`) all invoke `StageValidationService` before a state change is
committed. The validator rejects any request that leaves a stage in the same status,
so a HoD cannot submit another "start" action while the stage is already
`InProgress`. Because `ActualStart` is only set the first time a stage enters
`InProgress`, there is no direct edit path to correct a mistakenly entered start
date while the stage remains active.

To replace an incorrect start date, the HoD must temporarily move the stage to a
status that allows reopening—typically `Completed`, using the admin completion flow
if necessary—and then immediately submit a `status: "Reopen"` transition with the
corrected date. Reopening returns the stage to `InProgress` and overwrites
`ActualStart`. If needed, the HoD can then transition the stage back to the desired
status (for example, leaving it `InProgress` or completing it again with the proper
finish details). Every intermediate transition is recorded in the audit trail, but
this sequence is the supported approach given the current validation rules.
