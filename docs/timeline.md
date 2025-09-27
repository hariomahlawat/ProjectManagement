# Timeline pipeline

This document summarizes how timeline planning works after the latest updates.

## Roles

| Role | Capabilities |
| --- | --- |
| Project Officer | Edit timelines for assigned projects, save drafts, submit for approval. |
| Head of Department (HoD) | Review drafts, approve or reject timelines. |
| Admin | Can edit any project timeline and view approvals. |
| Viewer | Read-only access. |

## Plan states

Timeline plans transition through these states:

1. **Draft** – Created or edited by the Project Officer. Stored in `PlanVersions` and `StagePlans`.
2. **PendingApproval** – Set when the Project Officer requests HoD approval. Editing is locked until approval or rejection.
3. **Approved** – Set by the HoD. Copies plan data into `ProjectStages`, stamps the project with approval metadata, and creates a snapshot.
4. **Rejected** – Stored in `PlanVersions` via `RejectedOn`, `RejectedByUserId`, and `RejectionNote`. Status reverts to `Draft` so the Project Officer can continue editing.

Only one open plan (Draft or PendingApproval) exists per project.

## Pages

- **Projects / Overview**
  - Shows current `ProjectStages` timeline.
  - Displays badges for Pending Approval, Backfill Required, and Approved dates.
  - Provides Review and Edit buttons depending on role and state.
- **Timeline Edit (Project Officer)**
  - Supports duration-based generation and exact date editing.
  - “Save & request approval” submits to the HoD after validation.
- **Timeline Review (HoD)**
  - Shows draft vs. current differences.
  - Approve always stamps and snapshots, even if no changes.
  - Reject returns the draft to the Project Officer with optional notes.

## Key behaviors

- Pending approval locks Project Officer editing both in the UI and server-side.
- Approval publishes draft stage dates into `ProjectStages`, captures a snapshot, and records approval metadata on the project and plan.
- Rejection leaves the draft editable and records who rejected and why.
- Approvals are allowed even when there are no date differences.
- Backfill requirements block approval until resolved.
- All forms use anti-forgery tokens; role checks are enforced on GET and POST.

## Data and auditing

- Draft plan data lives in `PlanVersions` and related `StagePlans` rows.
- Live timeline uses `ProjectStages`.
- Snapshots are stored in `ProjectPlanSnapshots` and rows.
- Approval events are logged via `_logger` in `PlanApprovalService` and tracked with timestamps on the plan and project.

## QA checklist

1. **Project Officer (assigned)**
   - Save draft via durations or exact dates.
   - Submit for approval; verify pending badge and edit button hidden.
2. **HoD approval**
   - Approve draft with and without date differences.
   - Confirm snapshot exists and approval badge shows date.
3. **HoD rejection**
   - Reject draft with and without notes.
   - Verify flash message and that Project Officer can edit again.
4. **Backfill guard**
   - With unresolved backfill, approval button disabled and server returns friendly error.
5. **Unauthorized editing**
   - Non-assigned Project Officer receives 403 on POST.
6. **Pending lock enforcement**
   - Attempting to edit while pending shows friendly error toast.
7. **Audit/logging**
   - Review logs for approval/rejection entries.
