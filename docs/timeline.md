# Timeline Pipeline — Current (Hardened)

## Roles
- **Project Officer (PO)**: may edit only assigned projects; creates Draft plans; **Save** stays Draft; **Save & request approval** → PendingApproval.
- **HoD**: reviews Draft vs Current; **Approve** publishes to live + snapshots + stamps; **Reject** returns to Draft with note.
- **Admin**: read-only for approvals; can view all.
- **Viewer**: read-only.

## States
- **Draft** → stored in `PlanVersions` + `StagePlans` (not visible on Overview timeline).
- **PendingApproval** → locks PO edits until HoD acts.
- **Approved** → StagePlans published to `ProjectStages`; snapshot saved; project stamped with PlanApprovedAt/By.
- **Rejected** → we record RejectedOn/By/Note; status changed back to Draft.

## Pages
- **Overview**: chips for “Draft pending approval”, “Backfill required”, “Approved on …”; actions:
  - **HoD**: “Review & approve”
  - **Admin/assigned PO**: “Edit timeline” (hidden while PendingApproval)
- Stage rows display planned/actual dates, auto-completion badges and backfill flags sourced from `ProjectTimelineReadService`.
- **Edit timeline** (PO):
  - **Durations**: Calculate → writes to Draft StagePlans
  - **Exact**: direct edits to Draft StagePlans
  - **Save** (Draft) vs **Save & request** (PendingApproval)
  - Durations honour `ProjectScheduleSettings` (anchor date, weekend/holiday policy, next-stage start rules) and populate `ProjectPlanDuration` rows for future reuse.
- **Review** (HoD):
  - Diff = Draft vs Current; union of stage codes; highlight changes
  - **Approve** always allowed (unless backfill blocks)
  - **Reject** returns to Draft with optional note
  - Approvals invoke `PlanApprovalService`, which publishes `StagePlan` data into `ProjectStages`, records a snapshot and stamps the project with the approving HoD and timestamp.

## Security
- No inline scripts.
- Server-side role checks on all POSTs.
- Antiforgery tokens on forms.
