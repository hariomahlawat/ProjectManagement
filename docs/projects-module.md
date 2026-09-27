# Projects module storyboard

The Projects module combines procurement data, stage execution controls, plan collaboration, remarks, documents and media into a single workspace (`Pages/Projects/Overview`). This storyboard walks through each step, who performs it, and which services enforce it. It was checked against the code on 2026-09-27. Code is authoritative, and references name the type or member rather than line numbers.

## Personas and permissions

| Persona | Typical responsibilities | How access is enforced |
| --- | --- | --- |
| **Project creators** | Create projects, choose categories, seed historical stage completions, assign the initial HoD and PO. | `Pages/Projects/Create` requires the `Project.Create` policy, which allows Admin and HoD (`Policies.Projects.CreatorRoles`). |
| **Heads of Department (HoD)** | Decide PO stage requests, apply stage changes directly, approve plans, moderate documents. | `Stages/DecideChange` allows Admin and HoD (`ApprovalAuthorization.CanApproveProjectChanges`). `Stages/ApplyChange` is HoD-only (`[Authorize(Roles = "HoD")]` plus an in-handler check). **Any** HoD may act on any project; being the assigned HoD is not required. |
| **Project Officers (PO)** | Submit stage updates, maintain procurement facts, prepare plan drafts, add remarks and comments. | Stage requests: `Stages/RequestChange` (`Project Officer` role), and `StageRequestService` also requires the caller to be the project's `LeadPoUserId`. Plan drafts are scoped to `OwnerUserId` (`PlanDraftService`). Media and documents: assigned PO only (`ProjectAccessGuard.CanManageProjectMedia` / `CanManageProjectDocuments`). **Procurement:** see the gap below. |
| **Administrators** | Reassign the HoD/PO, approve plans and document requests, general oversight. | `AssignRoles` allows Admin and HoD and keeps the project row version for concurrency. Admin may approve or reject plans (`PlanApprovalService.EnsureCanDecide`) and decide stage requests. |

Every authenticated user can open the overview and read project information, documents, media and remarks (`ProjectAccessGuard.CanViewProjectInformation`). Writes are checked per persona as described above.

> **Authorisation gap:** `Pages/Projects/Procurement/Edit.cshtml.cs` (`[Authorize(Roles = "Admin,HoD,Project Officer")]`) does not check `LeadPoUserId`. Any user with the Project Officer role can therefore update IPA, AON, BM, L1, PNC and supply-order facts on any project. Every other PO write path is limited to the assigned project.

## Lifecycle at a glance

1. **Create and seed the project**
   The create form captures name, case file number, description, category, historical stage completion, the Repeat Build flag (`IsBuild`) and the initial HoD/PO. The handler enforces a unique case file number, rejects future completion dates, back-fills an initial `ProjectStage` for ongoing projects, and writes an audit entry. Repeat Build projects do not get a `ProjectTot` row. (see `Pages/Projects/Create.cshtml.cs`)

2. **Arrive on the overview workspace**
   `OverviewModel` (split across `Overview.cshtml.cs`, `Overview.Tot.cs`, `Overview.Content.cs`, `Overview.MultiJdp.cs`, `Overview.Presentation.cs`) loads the project, category breadcrumb, stage ledger, procurement summary, timeline, plan state, ToT summary, media, and pending meta-change requests.

3. **Clarify leadership**
   Admins and HoDs reassign the HoD and PO from the overview off-canvas (`Pages/Projects/AssignRoles.cshtml.cs`). The row version surfaces concurrency conflicts, and the change is audited.

4. **Track procurement milestones**
   `ProjectFactsService` upserts IPA, AON, Benchmark, L1, PNC and supply-order facts, clears the matching backfill flags, and audits each change. `Procurement/Edit` accepts a fact only after its gating stage is completed (`ProcurementStageRules`). IPA cost is read-only once the project appears in a published ARPP (`ArppPublishedEntries`). The supply-order "not in the future" check compares against the UTC date (`DateTime.UtcNow`), not the IST date.

5. **Plan collaboratively**
   `PlanReadService` (`Services/Stages`) prepares the exact-date and duration editors. `PlanDraftService` keeps one Draft per user per project (a unique filtered index on `PlanVersion(ProjectId, OwnerUserId)`). `PlanApprovalService.SubmitAsync` refuses a new submission while any plan for the project is `PendingApproval`. This check is done in code only, with no database constraint (see `docs/timeline.md`).

6. **Submit, review and apply stage updates**
   - *PO update:* The assigned PO submits one or many proposed stage updates through `StageRequestService`, and can keep submitting while earlier proposals await review.
     - Validation runs against a projected lifecycle: approved state, plus the latest pending proposal per stage, plus the new rows.
     - Each stage appears at most once per submission. A later submission supersedes only that stage's earlier pending proposal, and untouched downstream pending proposals are revalidated.
     - A pending start that is later revised to completion keeps the proposed start, recovered from the superseded request.
     - Notes are mandatory for block, skip, resume and reopen. Official status changes only on approval.
   - *Decision:* `StageDecisionService` (Admin or HoD) enforces transition policy, clamps inconsistent dates, and logs decisions.
   - *Direct apply:* `StageDirectApplyService` (any HoD) resolves the project's workflow version and validates workflow-specific predecessors. It may auto-complete unresolved predecessors with mandatory backfill. An authorised HoD may complete a stage without a completion date, which leaves the stage operationally complete but administratively incomplete until backfilled. Superseded PO requests are audited.

7. **Apply the project-specific workflow**
   Stage order and predecessors come from `IProjectStageWorkflowPolicy` (`ProjectStageWorkflowPolicy`) for the project's `WorkflowVersion`: SDD-1.0 runs FS → IPA → SOW → AON, and SDD-2.0 runs FS → SOW → IPA → AON. After AON both versions share the same dependency graph: BID follows AON; TEC and BM both follow BID; COB needs both TEC and BM; PNC and EAS follow COB; then SO → DEVP → ATP → PAYMENT → TOT. The graph is read from `StageDependencyTemplates`, with `ProjectStageWorkflowPolicy.BuildFallbackDependencies` used when no templates exist. `StageDateSuggestionResolver` suggests dates from this graph.

8. **Monitor execution health**
   `StageHealthCalculator` (`Models/Execution`) computes slip per stage. Seven or more days late makes the project Red. One to six days late, or an open stage due within two days, makes it Amber. Completed and skipped stages do not trigger the proactive Amber rule.

9. **Capture remarks and comments**
   - **Remarks** (`Services/Remarks/RemarkService`, API in `Features/Remarks/RemarkApi.cs`, contract in `docs/openapi/remarks.yaml`) have types Internal, External and Conference, and scopes General and TransferOfTechnology. They support mentions, a 3-hour author edit window, HoD/Comdt/Admin overrides, HTML sanitisation, audit snapshots and notifications.
   - **Comments** (`Services/ProjectCommentService`) are threaded, typed (Update, Risk, Blocker, Decision, Info) and can be pinned. Attachments are allow-listed and capped at 25 MB per file (`MaxAttachmentSizeBytes`), stored under the upload root (`IUploadRootProvider`). Only the author may edit or soft-delete a comment.
   - **Exception:** `OverviewModel`'s ToT remark handler (`Overview.Tot.cs`) inserts `Remark` rows directly. It bypasses `RemarkService`, so there is no sanitisation, no audit row and no notification.

10. **Moderate project documents**
    Admins, HoDs and the assigned PO submit upload, replace and delete requests (`Documents/UploadRequest`, `ReplaceRequest`, `DeleteRequest`; `DocumentRequestService`). Files are staged via `DocumentService.SaveTempAsync`. **Any** Admin or HoD can approve through `Documents/Approvals` (`ApprovalAuthorization.CanApproveProjectChanges`); assignment to the project is not required. `DocumentDecisionService` publishes, overwrites or archives, and `DocumentNotificationService` notifies stakeholders. ToT-linked document requests are refused for projects where ToT is not applicable (`ProjectTotApplicabilityPolicy`).

11. **Photos and videos**
    Admins, any HoD and the assigned PO manage media (`ProjectAccessGuard.CanManageProjectMedia`). `ProjectPhotoService` validates, optionally crops, and generates derivatives named `{storageKey}-{size}` under the project folder of the upload root. Setting a cover clears previous covers and bumps `CoverPhotoVersion`; deleting the cover promotes the next photo. All operations are audited.

    **Accessibility manual QA – photo reorder:** open **Projects → Photos → Reorder** with at least two photos. Press Space to grab a card, use the arrow keys to move it, and press Escape to cancel. Confirm the live-region announcements and that focus returns to the moved card after a drag and drop.

12. **Repeat Build projects (`Project.IsBuild`)**
    Repeat Build (re-manufacture) projects are instances of an existing capability.
    - **ToT:** not applicable (`ProjectTotApplicabilityPolicy`). Excluded from the tracker, summaries, dashboards, search and Compendium. Submit and approve are blocked. The overview shows "Not applicable — Repeat Build". Migration `20261216210000_RemoveTotDataFromRepeatBuildProjects` deleted ToT rows, requests and ToT-scoped remarks for projects that were Repeat Build at migration time.
    - **IPR:** cannot be linked (`IprProjectEligibilityPolicy`). When a project is switched to Repeat Build (`Meta/Edit`, `ProjectMetaChangeDecisionService`), `IprProjectLinkMaintenance.DetachLinkedRecordsAsync` unlinks its IPR records.
    - **Proliferation:** new records and new counting-rule preferences are refused (`ProliferationProjectEligibility`, `ProliferationSubmissionService`). Historical rows stay and are flagged by `ProliferationDataQualityService` (`RepeatBuildLinkCount`).
    - **FFC:** Repeat Build projects may be linked.
    - **Completed-project completeness:** a missing ToT status is not counted as a defect (`CompletedProjectPortfolioPolicy`).

    **Gap:** switching an existing project to Repeat Build does not clean up its ToT data the way the migration did. Its `ProjectTot` row, any pending `ProjectTotRequest` and ToT remarks remain, but they are hidden from the approvals queue (`ApprovalQueueService.BuildTotRequestQuery` filters `!project.IsBuild`) and from the tracker. A pending request on such a project therefore cannot be decided through the UI.

13. **Audit**
    Project creation, role assignment, procurement updates, stage requests and decisions, plan decisions, remarks, comments, documents and media all write audit events.

## Updating this storyboard

When new functionality lands, extend the relevant step with the responsible persona and how access is enforced, the happy path with validations and side effects, and any concurrency controls or audit outputs. Cite types and members, not line numbers.
