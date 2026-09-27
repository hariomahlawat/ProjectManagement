# Timeline pipeline

Checked against the code on 2026-09-27. Main types: `PlanDraftService`, `PlanApprovalService`, `PlanGenerationService`, `PlanCalculator` (`Services/Plans`, `Services/Stages`); `PlanReadService`, `PlanCompareService`, `PlanSnapshotService`, `ProjectTimelineReadService`, `StageActualsUpdateService`; pages under `Pages/Projects/Timeline`.

## Roles
| Role | Plans | Actual dates |
| --- | --- | --- |
| **Project Officer** | Edits drafts only for projects where they are `LeadPoUserId` (`Timeline/EditPlan`). Can **Save** a private draft or **Save & request approval**. | Assigned PO may edit actuals (`Timeline/EditActuals`). |
| **HoD** | May create a private draft, and review, approve or reject submissions for any project (`Timeline/Review`, `[Authorize(Roles = "Admin,HoD")]`). Direct stage application is covered in `docs/projects-module.md`. | Any HoD. |
| **Admin** | Same approve/reject rights as HoD (`PlanApprovalService.EnsureCanDecide` → `ApprovalAuthorization.CanApproveProjectChanges`). Admin is **not** read-only. | Yes. |
| **Others** | Read-only. | — |

`Timeline/Historical` (backfilling historical stage records) is limited to Admin and HoD.

## States (`PlanVersionStatus`)
- **Draft**: stored in `PlanVersions` and `StagePlans` and not shown on the overview timeline. Each user has at most one Draft per project, enforced by a unique filtered index on `(ProjectId, OwnerUserId)` where `Status = 'Draft'`.
- **PendingApproval**: set by `PlanApprovalService.SubmitAsync`. The submission is refused when any plan for the project is already pending. This is a read-then-write check with no database constraint, so two simultaneous submissions could both succeed.
- **Approved**: `StagePlans` are published into `ProjectStages`, a snapshot is saved, and the project is stamped with the approver and time.
- **Rejected**: `RejectedOn`, `RejectedBy` and the note are recorded, and the plan returns to Draft (`SubmittedByUserId` is cleared).

## Pages
- **Overview** shows chips for "Your draft saved", "Draft pending approval", "Backfill required" and "Approved on …". HoD and Admin get **Review & approve** while something is pending. The assigned PO, HoD and Admin get **Edit timeline**. Stage rows show planned and actual dates, auto-completion badges and backfill flags (`ProjectTimelineReadService`).
- **Edit timeline** (`EditPlan`):
  - Completed and skipped stages are collapsed by default.
  - **Durations** mode calculates exact dates from `ProjectScheduleSettings` (anchor, weekend/holiday policy, next-stage start rule) and stores `ProjectPlanDuration` rows.
  - **Exact** mode edits `StagePlans` directly. The current stage's planned completion matters operationally; future dates are optional.
  - **Save & request** is blocked while another plan is pending (`PlanDraftLockedException` / "Another submission is already pending approval.").
- **Review** (`Timeline/Review`): shows the pending plan against the current plan (`PlanCompareService`). **Approve** refuses self-approval ("You cannot approve your own plan submission."). **Reject** returns the plan to Draft with an optional note.

## Workflow-version consistency
Stage order and dependencies come from `IProjectStageWorkflowPolicy` for the project's `WorkflowVersion`:
- **SDD-1.0:** FS → IPA → SOW → AON → BID → {TEC, BM} → COB → {PNC, EAS} → SO → DEVP → ATP → PAYMENT → TOT
- **SDD-2.0:** FS → SOW → IPA → AON → (then the same as SDD-1.0)

The graph comes from `StageDependencyTemplates`, falling back to `ProjectStageWorkflowPolicy.BuildFallbackDependencies`. Validation, direct application, decisions, predecessor cascade, auto-start, plan generation and date suggestions (`StageDateSuggestionResolver`) all use it.

## Actual dates and backfill
- **Completed stage:** the completion date is authoritative. Actual start is optional; when it is missing, duration is inferred from the preceding applicable stage's completion plus one day.
- **Current stage:** the actual start and planned completion are what matter.
- **Future stage:** planned dates are optional.
- **Skipped stage:** no dates are required.
- **Authorised completion override:** a HoD may complete a stage without a date. The workflow advances, and mandatory backfill stays until the date and any mandatory facts are recorded.
- **Timeline → Edit actual dates** (`EditActuals` → `StageActualsUpdateService`) changes dates directly and audits them, **without HoD approval**, for Admin, any HoD or the assigned PO. Stage status is not changed.

## PO projected lifecycle
The assigned PO can submit and revise updates for several stages while earlier updates await approval (`StageRequestService`). The update modal shows the projected lifecycle, distinguishing existing pending updates, new updates and the current revision. A pending start that is later revised to completion is recovered from the superseded request history and applied when the completion is approved. Official progress reflects approved stage records only.

## Security
- Server-side role and assignment checks on every POST; antiforgery on forms (`[AutoValidateAntiforgeryToken]` on the timeline page models); no inline scripts.
