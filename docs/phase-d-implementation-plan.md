# PRISM ERP — Phase D Implementation Plan: Approved Actual Correction and Completion Backfill

> **Status:** Review draft. This is a plan, not authorization to implement.
>
> **Baseline:** `master` at `4e0e9769`, which includes #2667–#2671. The documentation baseline is `24bfdf70`.
>
> **Scope:** Investigation and design only. No production code, migration or PR is part of this document. Production deployment state, database version and configuration were **not** inspected and remain unverified.
>
> **Revision 2** (architecture-review corrections). Changes from revision 1:
>
> - **Transactional evidence contract.** Required history now commits with the change; `AuditLogs` is written after commit as secondary telemetry (E.1).
> - **Rollback.** Governance-safe rollback: R-D2 refuses PO direct publication, and fix-forward is the default recovery (I).
> - **Concurrency.** A server-issued editor baseline covers first submissions as well as replacement of a pending revision (E.4).
> - **Validation scope.** A complete mutation-boundary matrix, with acceptance criteria scoped to it (F.5, J).
> - **Open decisions.** Reconciled, with only true blockers marked (K).

---

## 0. Executive recommendation

**Build a small dedicated proposal model for Actual-date facts.** It should be project-scoped and immutable per revision. Keep it separate from `PlanVersion` and from `StageChangeRequest`. Deliver it in three releases.

- **Release D-1: shared Actual final-state validation and publication.** No schema change.
  - Extract one source-neutral `IActualTimelineValidationService` and one shared publication routine.
  - Enforce them inside the existing project transaction gate on today's direct paths: `StageActualsUpdateService` and `StageBackfillService`.
  - This closes gaps G12 and G13 for every caller, including HoD/Admin. Rollback is a binary swap.
- **Release D-2: additive schema plus the PO authority cut-off.**
  - Add two new tables, `ActualCorrectionRevisions` and `ActualCorrectionItems`, plus their EF mapping and migration. The tables stay unused.
  - The same binary stops PO-only users publishing Actual dates directly; HoD/Admin direct correction is unchanged.
  - The startup migrator rejects older binaries once a new migration is applied (`DatabaseStartupMigrator.EnsureNoUnknownAppliedMigrations`). Isolating the schema step lets D-3 roll back by binary swap.
  - Because R-D2 already refuses PO direct publication, that swap never restores direct PO publication (section I).
- **Release D-3: PO proposals and HoD decision.**
  - An assigned PO's submissions through the existing `EditActuals` and `BackfillApply` routes become **proposals**.
  - HoD decides them in the existing Decision Centre (`Pages/Approvals/Pending`).
  - Approval re-validates the exact revision against current official facts inside the project gate, then publishes through the same routine HoD/Admin use directly.
  - HoD/Admin direct correction remains, with identical validation.
  - R-D2 and R-D3 are intended to deploy in one maintenance window. R-D2 then runs on its own only as the emergency recovery floor.

The key choices:

- **At most one actionable Actual-correction revision per project.** It is project-scoped, not stage-scoped.
- **No server-side Draft state.** Each PO save is a validated submission that supersedes the previous pending revision as a whole.
- **Exact-revision decisions** use compare-and-set on the submission epoch.
- **A server-issued editor baseline.**
  - It covers the actionable-revision identity, including "none", plus per-stage official values.
  - Every PO submission and direct save checks it, so a stale form can neither supersede a newer revision nor submit against an unrecognised official state (E.4).
- **Required history commits atomically with the change it records.**
  - Required history is the revision status/closure fields, the items and the `StageChangeLog` rows.
  - Notifications and the secondary `AuditLogs` entry run only after commit (E.1).
- **ARPP authority is enforced at submission and at decision.**

These choices satisfy rules 18–26 for the Actual category without building the Phase E cross-category Timeline Change Set. They also avoid parallel per-stage Actual lineages, which Phase E would otherwise have to unwind.

**Validation scope** (F.5):

- Phase D **enforces** shared final-state validation on every Actual-date *correction and backfill* boundary: direct, proposal submission and approval.
- Lifecycle writers stay **warning-only**: `StageDirectApplyService`, `StageDecisionService` and `HistoricalStageRecordService`. Enforcing it there would change lifecycle semantics, so it is deferred.
- ARPP synchronization is **exempt by rule 17**.
- Phase D does not claim that every authoritative Actual write is validated.

---

## A. Executive assessment

### A.1 What exists today

| Capability | Where | Observed behaviour |
| --- | --- | --- |
| Generic Actual correction | `EditActualsModel.OnPostAsync` → `StageActualsUpdateService.UpdateAsync` | Admin, **any** HoD, or the project's `LeadPoUserId` publishes directly to `ProjectStage`.<br>Validates each changed row: status/date matrix, IST future limit, start ≤ completion, and the predecessor boundary via `StageDateSuggestionResolver`.<br>Rejects stages that have a pending `StageChangeRequest`. Enforces published-ARPP IPA completion.<br>**No transaction, no project gate, no stale check, and untouched successors are not checked.** |
| Completion backfill | `BackfillApplyModel.OnPostAsync` (JSON) → `StageBackfillService.ApplyAsync` | Admin/HoD, or the assigned PO, publishes directly.<br>Requires `Completed` with `CompletedOn == null`. Checks IST limits and local ordering. Refuses managed IPA.<br>**No graph chronology, no pending-request guard, no gate, no `IAuditService` entry.** |
| Lifecycle proposals | `StageRequestService.CreateBatchAsync`, `StageDecisionService.DecideAsync` | Per-stage pending request, protected by the filtered unique index `ux_stagechangerequests_pending`.<br>Same-stage supersession. Projected validation of the whole batch, including untouched pending requests.<br>Decision claims the request with `ExecuteUpdate`, inside the gate. |
| HoD direct lifecycle | `ApplyChangeModel` (HoD) → `StageDirectApplyService.ApplyCoreAsync` | Gate plus transaction. Supports authorised completion without a date (`RequiresBackfill = true`) and forced predecessor backfill. |
| Plan proposals (B1/B2/C1/C2) | `PlanApprovalService`, `PlanDraftService`, `PlanRevisionConsistency` | Exact-revision decision with an epoch check (`SubmittedOn`). Project advisory gate via `LockProjectAsync`.<br>Material supersession, withdrawal, edit fingerprints (`PlanEditFingerprint`), and after-commit notifications through `RelationalTransactionScope.RegisterAfterCommit`. |
| ARPP authority | `ArppIpaStageAuthorityService`, `ArppIpaStageSynchronizer`, `StageEffectiveState` | Published entries make IPA `Completed` with `CompletedOn = IssueDate`. Draft entries do not.<br>Synchronization runs inside the ARPP verify/reconcile transaction and at startup. **It does not take the project gate.** |
| Graph | `IProjectStageWorkflowPolicy` → `ProjectStageWorkflowSnapshot` | Configured `StageDependencyTemplates`, or SDD-1.0/2.0 fallback topology. The effective EAS→PNC edge depends on published `Project.PncApplicable`. Optional stages come from `StageTemplates`. |

### A.2 What is missing (relative to `docs/timeline.md`)

1. PO Actual corrections and backfill are not proposals. This is the deviation from rules 19 and 22–24 recorded as G03/G04.
2. Validation does not cover the final state of untouched descendants (G13), and backfill has no graph check at all (G12).
3. There is no stale-save protection for direct Actual edits (rule 20), and no transaction or gate on those writers.
4. There is no immutable proposal evidence for Actual facts (rule 26).
5. Convergence handling has a gap. When any required branch is unresolved — for example a predecessor completed without a date — `StageDateSuggestionResolver.Resolve` returns **no boundary at all**. The dated branches are then not enforced either. Rule 6 says the latest applicable predecessor completion controls; a known dated branch should still bind. See F.3.

### A.3 What can be reused

| Item | Reuse |
| --- | --- |
| `PlanRevisionConsistency.LockProjectAsync` | The project gate, reused verbatim. The same key namespace serializes with plan publication, `StageDirectApplyService` and `StageDecisionService`. |
| `RelationalTransactionScope` | Transactions, savepoints and `RegisterAfterCommit`. |
| C1 compare-and-set | The `ExecuteUpdate … WHERE Status = Pending AND SubmittedOn = expected` claim pattern. |
| `IProjectStageWorkflowPolicy` and `StageDateSuggestionResolver` | Branch traversal, including skipped stages. The validator reuses the traversal and changes only the aggregation (F.3). |
| `StageEffectiveState` and `IArppIpaStageAuthorityService` | Unchanged. |
| Decision Centre | `ApprovalQueueService`, `ApprovalDecisionService`, and `ApprovalDecisionRequest`, which already carries `SubmittedOn` for epoch binding. |
| Test fixtures | `PostgresMigrationIntegrationTests` with its fixture pattern (`PlanRevisionConcurrencyPostgresFixture`), `Fakes/FakeClock`, and `StageWorkflowTestFactory`. |

### A.4 Principal risks and choices

- **Schema change is genuinely required** (D.1). Existing tables cannot hold project-scoped multi-stage Actual revisions without semantic overloading, which older binaries would misread.
- **Migration irreversibility at the binary level.** Once a migration is applied, older binaries refuse to start, so the release sequencing in section I matters.
- **ARPP synchronization runs outside the project gate.** Approval must take row locks on the project's stages (E.3).
- **Tightened validation can newly reject operations** that pass today. This is mitigated by blocking only violations that depend on a *changed* value (F.4).
- **Audit placement inside PostgreSQL transactions.**
  - `AuditService.LogAsync` saves through the shared request `ApplicationDbContext`.
  - It then runs a raw-SQL daily-summary upsert whose failure it swallows. Inside an outer PostgreSQL transaction, that swallowed failure would leave the transaction aborted.
  - Phase D therefore keeps its required evidence in domain tables written in the transaction, and writes `AuditLogs` after commit (E.1).
- **Rollback can reverse governance.** A binary swap that restores PO direct publication is technically possible but is not an acceptable production rollback. R-D2 closes PO direct publication, so no post-D-2 rollback target reopens it (section I).
- **Phase E scope creep.** Plan, lifecycle and Actual stay separate categories. The Actual lineage is project-scoped so that Phase E can absorb it (section C.9).

---

## B. Current architecture and execution map

### B.1 Actual-date command paths (all authoritative writers found)

| # | Entry point (authorization) | Service / method | Fields written | Tx / gate | Log / audit |
| --- | --- | --- | --- | --- | --- |
| 1 | `Pages/Projects/Timeline/EditActuals` POST (`[Authorize(Roles="Admin,Project Officer,HoD")]`; Admin, any HoD, or `LeadPoUserId`) | `StageActualsUpdateService.UpdateAsync` | `Status` (only for ARPP-effective IPA), `ActualStart`, `CompletedOn`, `RequiresBackfill`, auto flags | none / none | `StageChangeLog[ActualsUpdated]`, then `Projects.ActualsUpdated` audit **after** save (non-atomic) |
| 2 | `Pages/Projects/Stages/BackfillApply` POST JSON (`Admin,HoD,Project Officer`; PO must be lead) | `StageBackfillService.ApplyAsync` | `ActualStart`, `CompletedOn`, `RequiresBackfill = false`, auto flags | none / none | `StageChangeLog[Backfill]` |
| 3 | `Stages/ApplyChange` (HoD) | `StageDirectApplyService.ApplyCoreAsync` | all lifecycle and date fields; forced predecessor backfill | yes / yes | `DirectApply`, `Applied`, `AutoBackfill`, `Superseded` |
| 4 | `Stages/DecideChange`, Decision Centre (Admin, HoD) | `StageDecisionService.DecideAsync` → `StageProgressService.UpdateStageStatusAsync` | lifecycle, carried-forward start, completion cascade | yes / yes (approve path) — **but validation runs before the gate** | `Approved`, `Applied`, `AutoBackfill`, `Rejected` |
| 5 | `Timeline/Historical` (Admin, HoD; legacy terminal projects only) | `HistoricalStageRecordService.SaveAsync` | all | yes / no | `Backfill`, audit |
| 6 | ARPP verify/reconcile, startup | `ArppIpaStageSynchronizer.SynchronizeProjectsAsync` / `SynchronizeAllAsync` | IPA `Status`/`CompletedOn`/flags | caller's Tx / **no gate** | `Superseded` for requests only; **no stage log for the completion itself** |
| 7 | `Procurement/Edit` (`Admin,HoD,Project Officer`; no assignment check before #2673) | `ProjectFactsService.ClearBackfillFlagAsync` | `RequiresBackfill = false` (only when dates already present) | none | fact audit |
| 8 | `Projects/Create` (`Project.Create` policy) | `CreateModel.OnPostAsync` | initial completed stage with `ActualStart = CompletedOn` | none | `Projects.Created` |

There is no minimal API, controller or hosted worker that writes Actual dates. The project purge (`ProjectModerationService`) deletes stage rows.

Paths 1 and 2 are the Phase D PO surfaces. Paths 3–8 are HoD/Admin, system or creation paths. Phase D leaves their authority unchanged (see C.7).

### B.2 Authorization boundaries (as implemented)

- **Assigned PO** means `Project.LeadPoUserId == userId` (EditActuals, BackfillApply, StageRequestService). There is no other PO assignment concept on these paths.
- **HoD** means anyone with the `HoD` role. No path checks `Project.HodUserId`; `StageDirectApplyService` documents this deliberately.
- **Decisions** use `ApprovalAuthorization.CanApproveProjectChanges(isAdmin, isHoD)`, so **Admin can decide** stage and plan requests today. Plan decisions block self-approval; stage decisions do not.
- **UI capability** is `OverviewModel.Access.CanEditTimeline = Admin || HoD || assigned PO` (`Pages/Projects/Overview.Presentation.cs`). This is visibility only (G02).

### B.3 Validators

| Validator | Domain | Graph-aware | Final-state | Notes |
| --- | --- | --- | --- | --- |
| `StageActualsUpdateService` (inline) | Actual | predecessor boundary only | partial: the projection includes submitted completions | changed rows only |
| `StageBackfillService` (inline) | Actual | no | no | — |
| `StageValidationService` | lifecycle transition | predecessors via `RequiredPredecessorClosure` | single stage | uses the resolver |
| `StageRequestService` (projected) | lifecycle proposals | yes | batch plus untouched pending | — |
| `IPlannedTimelineValidationService` | Planned start/PDC | yes | complete candidate | the B2 model for a source-neutral candidate |

### B.4 Notification behaviour

- Plan services register notifications after commit (C2).
- `StageProgressService` publishes through `INotificationPublisher`, which calls `SaveChanges` on the same context and so participates in the ambient transaction.
- Actual corrections and backfill send no notifications today. Lifecycle requests do not notify either; HoD discovers them in the Decision Centre.
- **`IAuditService` placement varies:**
  - ARPP services register their `AuditLogs` writes with `RelationalTransactionScope.RegisterAfterCommit` (`ArppCommandService`, `ArppReconciliationService`, `ArppIpaStageSynchronizationAudit`).
  - Plan and remark services use the same mechanism for notifications (`PlanApprovalService.SubmitAsync`, `PlanDraftService`, `RemarkService`).
  - In these services the durable decision history lives in domain tables such as `PlanApprovalLog`, written inside the transaction.
  - `StageProgressService.UpdateStageStatusAsync` and `HistoricalStageRecordService.SaveAsync` call `LogAsync` inside their transaction, before commit.
  - `StageActualsUpdateService` calls it after its non-transactional `SaveChanges`.
  - `RelationalTransactionScope` runs post-commit callbacks only after the root commit, and collects their failures in `PostCommitErrors` without failing the committed operation.

### B.5 Cross-module dependencies on Actual facts

Readers include:

- `ProjectTimelineReadService`, `TrackerBuilder`, `ProjectLifecyclePositionResolver` and `PresentStageHelper`.
- Progress Review reports, `ArppFyProjectUpdateService`, analytics, `ScheduleEngine` forecasting and `StageHealthCalculator`.
- Cost resolvers, workspaces, `SearchProjectionBuilder` and FFC.

None of them may read proposals. Phase D keeps every reader on official `ProjectStage` values, except the timeline and editor read models, which add a separate "Proposed" dimension (rule 23).

### B.6 Documentation discrepancies found

| # | Discrepancy | Significance |
| --- | --- | --- |
| 1 | `docs/engineering/migrations.md` says startup applies both migration sets (verified in `DatabaseStartupMigrator`). `docs/deployment/offline-ws2022.md` §A3 says "do not auto-apply migrations; apply reviewed SQL". | The rollout plan must state which governs. Section I assumes the startup gate is authoritative and treats a reviewed SQL script as a pre-check artefact. |
| 2 | `docs/data-domain.md` says backfill guards live in `PlanApprovalService.ApproveLatestDraftAsync`. `Services/Plans` contains no backfill reference. It also still describes a pre-C2 "read-only until HoD acts" lock. | The doc is stale. Correct it in D-1. |
| 3 | Rules 19 and 22 frame HoD as the approver and say Admin gains no new approval rights. Code lets Admin decide stage and plan requests. | Open decision K-1. |
| 4 | `StageDateSuggestionResolver` suppresses every boundary when any convergence branch is undated. | Validity gap versus rules 6/8 (F.3). |
| 5 | `StageDecisionService.DecideAsync` runs `StageValidationService` before `LockProjectAsync` (lines ~295 and ~312). | A stale validation window against concurrent Actual publication. Fixed in D-1c (E.4). |
| 6 | `Pages/Projects/Procurement/Edit.cshtml.cs` has no project-assignment check, yet can clear `RequiresBackfill`. `Project` has no global `IsDeleted` query filter, so `EditActualsModel` does not exclude soft-deleted projects. | Resource-authorization defects outside Phase D's core. The second is fixed in D-1. The first is fixed separately in hariomahlawat/ProjectManagement#2673 (open, not yet merged). |
| 7 | `StageProgressService` and `HistoricalStageRecordService` call `IAuditService.LogAsync` inside a PostgreSQL transaction (B.4). `AuditService.PersistDailyActionSummaryAsync` swallows its own failure. | A latent pre-existing risk: a swallowed statement failure aborts the surrounding PostgreSQL transaction. Phase D does not copy this pattern (E.1). Fixing the existing callers is a separate follow-up, not Phase D. |
| 8 | `CreateModel.OnPostAsync` creates the "last completed" stage with `ActualStart = CompletedOn`. | Deviates from rule 3 (do not invent an Actual start). It runs only at project creation, so it cannot race a proposal. Recorded as a separate defect (F.5). |

---

## C. Recommended domain design

### C.1 Alternatives evaluated

| Option | Assessment |
| --- | --- |
| **(a) Reuse `StageChangeRequest`** | Rejected. It is per stage (`ux_stagechangerequests_pending` allows one pending per stage), so a coordinated multi-stage revision with a single identity is impossible. `RequestedStatus` is parsed as a `StageStatus` by `ApprovalQueueService`, `StageRequestService` projections, `StageApprovalSequenceService` and `ProjectTimelineReadService`. A new pseudo-status would be misread by current code and by any older binary still running. The `DecisionStatus` CHECK constraint would also need to change. |
| **(b) Extend `PlanVersion`/`StagePlan`** | Rejected. Plan revisions carry planning policy, PNC applicability and B1 cutover markers. Adding Actual columns would let `ActionablePending`, supersession and `HasMaterialChanges` mix plan and actual semantics. Actual approval would then republish plan inputs, and plan approval would publish actuals. This is a premature Phase E under the plan's identity. |
| **(c) Reuse `ProjectMetaChangeRequest` (JSON `Payload`)** | Rejected. Its decision path applies project metadata. An unrecognised `ChangeType` reaching an old binary's decision code is a mixed-version hazard. An untyped payload also weakens evidence and querying. |
| **(d) Dedicated `ActualCorrectionRevision` + `ActualCorrectionItem`** | **Recommended.** Typed per-stage before/proposed evidence, project-scoped single actionable revision, exact identity, and no change to the meaning of existing tables. The cost is one additive migration. |
| **(e) Generic workflow/proposal framework** | Rejected as speculative. It would pre-empt the Phase E design. |

### C.2 Proposal ownership

- **Submitter:** a user in the `Project Officer` role who is the project's `LeadPoUserId`, and who is neither Admin nor HoD. Users holding Admin or HoD keep direct authority (C.7). This matches the current precedence in `EditActualsModel`.
- **Lineage:** one per project. While a revision is `Pending`, a new submission by the assigned PO supersedes it.
- **Change of lead PO:** if the lead PO changes while a revision is pending, the new lead PO may supersede or withdraw it. Validity is project-scoped, so the revision remains decidable. A former PO cannot withdraw. Open decision K-6.
- **Authorization is checked at every write, inside the gate.**
  - Assignment is re-read from `Project.LeadPoUserId` on every submit and withdraw, never taken from the form or the baseline token.
  - A PO reassigned away while their editor is open is therefore refused on the next submit (`Forbidden`). Their open form cannot be replayed by anyone else either, because the baseline token carries no authority (E.4).
- **From R-D2 onward, a PO-only user cannot publish Actual dates directly.** Before D-3 is deployed, `EditActuals` and `BackfillApply` refuse such posts (C.7, I.1).

### C.3 Revision states and identity

`ActualCorrectionRevision.Status` is a string with a CHECK constraint and these values:

- **`Pending`** — actionable. At most one per project, enforced by a filtered unique index.
- **`Superseded`** — replaced by a later revision. Terminal; `SupersededByRevisionId` is set.
- **`Withdrawn`** — withdrawn by the PO. Terminal.
- **`Approved`** — decided and published. Terminal.
- **`Rejected`** — decided, nothing published. Terminal.

The identity a HoD decides is the triple **(`Id`, `SubmittedOn`, `RevisionNo`)**.

- `RevisionNo` is allocated per project under the gate as `max + 1`, with a unique index on `(ProjectId, RevisionNo)`.
- The decision form posts `Id` and `SubmittedOn`, reusing the existing `ApprovalDecisionRequest.SubmittedOn`.
- `SubmittedOn` is set once and never rewritten. As in C1, a fast resubmission still gets a new row, so the epoch is a second guard rather than the only one.

**Immutability.** After insert, only the status-closure fields change: `Status`, `ClosedByUserId`, `ClosedOn`, `SupersededByRevisionId` and `DecisionNote`. Items are never updated. The service layer enforces this (there is no update path), and tests verify it.

**No Draft state.** The PO's working copy lives in the browser form. Every save is a validated submission. This deliberately differs from C2:

- An Actual correction is a few dates on a few rows. It has none of the planning policy that justified C2's private Drafts.
- Without Drafts there is no "material SaveDraft makes the old revision non-actionable" case, no private-draft visibility rules and no discard semantics.
- Rule 22 is met: the PO can create, revise, withdraw, resubmit and replace at any time; pending never blocks them.

The editor may keep an unsent form in `localStorage` as a convenience. It is not authoritative.

### C.4 Submission and resubmission (`ActualCorrectionProposalService.SubmitAsync`)

The input is:

- the project;
- the requested changes: stage code, `ActualStart`, `ClearActualStart`, `CompletedOn` and an optional per-item note;
- the **editor baseline** the form was rendered with (E.4): the actionable-revision identity (`BasedOnRevisionId` and `BasedOnSubmittedOn`, or explicit "none") and a per-stage `StageToken` for every row shown;
- an optional PO note;
- the entry point (`Editor` or `Backfill`).

1. Open a `RelationalTransactionScope` and call `LockProjectAsync`. Everything below runs inside the gate.
2. Authorize against the current project row. The PO must be the current `LeadPoUserId` and must not be Admin or HoD. The project must exist, not be deleted, be lifecycle `Active` and not be legacy, matching what the UI already shows. Otherwise return **Forbidden** or **NotFound**.
3. Check the editor baseline (E.4). On any failure, return **Stale** and write nothing:
   - The baseline marker is missing (a legacy or incomplete post): "This page is out of date. Reload to review the current dates before submitting."
   - The posted revision identity differs from the current actionable revision. This covers "none" against an existing pending revision, an existing one against "none", and one revision against another: "Your pending update changed in another window or was decided. Reload to review the latest values."
   - A stage in the resulting item set has an official value different from its posted `StageToken`. For carried-forward items, the item's `Before*` values are compared instead. "These stages changed since you opened the page: …. Reload and review." This check runs as soon as step 4 has built the item set, and before step 5.
4. Build the candidate item set.
   - From the **editor** entry point: the submitted set *is* the complete proposal. Any previously pending item the PO left at its official value is removed.
   - From the **backfill** entry point: carry forward the pending items and override or add the submitted stages (rule 24).
   - Normalize with the shared `ActualChangeNormalizer`: blank means preserve, explicit `ClearActualStart` means clear, and a missing `CompletedOn` never clears it.
   - Drop no-op items (proposed equals official).
5. Derive each item's `Kind` on the server; never trust the client.
   - `Backfill` when official is `Completed` with `CompletedOn == null` and a completion is proposed.
   - `Correction` otherwise.
   - A backfill-entry submission whose item would not be `Backfill` returns **Conflict**, preserving today's 409 meaning ("no longer requires backfill").
6. Validate with `IActualTimelineValidationService` (section F) using source `ProposalSubmission`. The validator also runs the pending-lifecycle contradiction check (F.6).
7. If the candidate equals the current pending revision's content, do nothing and return **Unchanged**. The HoD's review stays valid, as in C2.
8. If the candidate is empty, return Validation ("No changes. Use Withdraw to cancel a pending update.").
9. Otherwise, in the same transaction:
   - Conditionally close the previous pending revision with `UPDATE … SET Status = 'Superseded', SupersededByRevisionId, ClosedByUserId, ClosedOn WHERE Id = @prev AND Status = 'Pending'`. It must affect exactly one row; otherwise roll back and return **Stale**.
   - Insert the new revision and its items, with `Before*` values snapshotted from the locked official rows and `SupersedesRevisionId` set.
   - `SaveChanges` then `CommitAsync`. The supersession, the new revision and its items are the required evidence (E.1). They commit together or not at all.
   - Register the secondary `AuditLogs` entry (`Projects.ActualCorrectionSubmitted`) and the optional HoD notification with `RegisterAfterCommit`. Their failure never undoes the submission.

Official `ProjectStage` rows are never written on this path, which is the core invariant.

### C.5 Withdrawal (`WithdrawAsync`)

- Takes the exact `RevisionId` and `SubmittedOn`.
- Inside the gate, the caller must be the current assigned PO, re-read from the project row (K-6).
- A compare-and-set moves `Pending` to `Withdrawn` and sets `ClosedByUserId`, `ClosedOn` and an optional reason, in one statement inside the transaction. A stale or already-decided revision returns `NotActionable`.
- The secondary `AuditLogs` entry is written after commit.
- No official mutation.

### C.6 HoD approval and rejection

- **`ApproveAsync(revisionId, expectedSubmittedOn, approver)`**
  1. Authorize with `ApprovalAuthorization.CanApproveProjectChanges` (see K-1). Reject self-approval (`SubmittedByUserId == approver`).
  2. Open the transaction, take the gate, then `SELECT … FOR UPDATE` the project's `ProjectStages` rows (E.3).
  3. Re-read the revision. It must be `Pending` with matching `SubmittedOn` and project; otherwise return **Stale** ("A newer revision is available. Review the latest update before deciding.").
  4. **Baseline check:** for each item, the locked official `Status`, `ActualStart`, `CompletedOn` and `RequiresBackfill` must equal the item's `Before*` values. Any difference returns **SourceChanged**, and nothing is mutated.
  5. Resolve ARPP authority *after* the row lock. Validate the final state (official plus items) with source `Approval`.
  6. Compare-and-set claim: `UPDATE … SET Status = 'Approved', ClosedByUserId, ClosedOn, DecisionNote WHERE Id AND Status = 'Pending' AND SubmittedOn = expected`. It must affect exactly one row.
  7. Publish through `ActualFactPublisher.Apply` (C.7), which writes the stage fields and one `StageChangeLog` per stage.
     - The action is `Backfill` or `ActualsUpdated`, and `UserId` is the approver.
     - The note reads "Approved actual correction rev {RevisionNo} (#{Id}) submitted by {submitter} at {time}". It is built to fit `StageChangeLog.Note` (1024); the decision note is never truncated into silence, because the full text lives on the revision row.
  8. `SaveChanges` then `CommitAsync`. The claim, the stage fields and the `StageChangeLog` rows are one atomic unit (E.1).
  9. If anything in steps 2–8 throws, the scope is disposed without commit, so it rolls back. The claim, every stage field and every log row are undone; the revision stays `Pending` and the caller gets an error, not success. No notification is sent, because post-commit callbacks are discarded on rollback.
  10. After commit only: the secondary `AuditLogs` entry (`Projects.ActualCorrectionApproved`) and the PO notification, through `RegisterAfterCommit`.
- **`RejectAsync(revisionId, expectedSubmittedOn, note)`**
  - Same identity check (`Pending` with matching epoch) and compare-and-set to `Rejected`. The compare-and-set also sets `ClosedByUserId`, `ClosedOn` and `DecisionNote`, inside the transaction.
  - A note is required; the Decision Centre already requires remarks.
  - No baseline or validity check is needed, because nothing publishes. A HoD can therefore clear a revision whose basis has changed (K-3). The epoch check still applies, so rejecting N after N+1 exists fails as stale (rule 24).
  - No official mutation. `AuditLogs` and the notification follow after commit.
- Approval never changes `Status`. It does not run realignment, and neither direct correction nor backfill does today. Kept identical.

### C.7 Direct HoD/Admin authority

These paths are unchanged in *who* may act:

- `EditActuals` for Admin and HoD.
- `BackfillApply` for Admin and HoD.
- `StageDirectApplyService`, `StageDecisionService`, `HistoricalStageRecordService` and the ARPP synchronizer.

From R-D2, the only authority change is the **PO cut-off**:

- A user who is a PO but neither Admin nor HoD, posting to `EditActuals` or `BackfillApply`, no longer reaches `StageActualsUpdateService` or `StageBackfillService`.
- Until D-3 is deployed, the page returns a refusal: "Actual-date corrections now require HoD approval. Ask the HoD to apply this change." The JSON form is `403 { ok: false, error: "approval-required" }`.
- From D-3, the same check routes the post to `ActualCorrectionProposalService.SubmitAsync` instead.
- The check lives in the page model, next to the existing role and assignment checks. It adds no configuration key.

The direct Actual paths (1 and 2) are rebuilt on the shared components:

- **`ActualChangeNormalizer`**: input semantics.
- **`IActualTimelineValidationService`**: validity, with source `Direct`.
- **`ActualFactPublisher`**: stage field effects and `StageChangeLog` rows, written in the caller's transaction. The caller registers `AuditLogs` after commit (E.1).

They also gain the gate, a transaction, the row lock and the **per-stage `StageToken`** check (E.4).

- If a submitted row's token differs from the locked row, the save returns "This stage changed since you opened it. Reload and review."
- In R-D1, a direct post without the baseline marker keeps today's behaviour, for pages opened before deployment; the acceptance is logged.
- From R-D3, every Actual editor post (direct or proposal) must carry the marker. All pages served since R-D1 include it.

A direct HoD/Admin publication **does not** modify a pending PO revision. That revision's baseline check then fails at approval time, and the read model shows it as "Needs revision: official facts changed". This follows rule 24 and the precedent that stale decisions are rejected rather than edits blocked (K-4 lists the alternative).

`ActualFactPublisher.Apply(stage, change, actor, logAction, note)` must reproduce the existing effects exactly:

- Set `ActualStart` and `CompletedOn`.
- `RequiresBackfill`: for `Completed`, `!CompletedOn.HasValue`; `false` for ARPP-managed IPA.
- `IsAutoCompleted = false` and `AutoCompletedFromCode = null` on changed rows.
- Never change `Status`, except the existing ARPP-effective IPA case in `StageActualsUpdateService`, which writes the effective `Completed` status when a managed IPA start is corrected on a stale row. That case is preserved and documented as ARPP reconciliation.

### C.8 Interaction with lifecycle requests and ARPP

- **Pending lifecycle requests (rule 18).**
  - Today, `EditActuals` refuses stages with a pending `StageChangeRequest`. For **PO proposals**, Phase D replaces this lock with a contradiction check against the *projected* lifecycle state (F.6). The PO is never frozen, but cannot propose something incompatible with a pending lifecycle request that could still publish.
  - For **direct** HoD/Admin saves, keep the current conflict refusal unchanged, to avoid altering lifecycle precedence in Phase D.
  - Lifecycle decisions remain independent. If a lifecycle approval changes a stage covered by a pending Actual revision, that revision becomes `SourceChanged` at decision time.
- **ARPP.**
  - At submission and at approval, an IPA `CompletedOn` item is rejected while a published authority exists. The existing message is `ArppManagedIpaStageException.UserMessage`.
  - An IPA `ActualStart` item remains allowed (rule 23) and is validated against the effective `IssueDate`.
  - Draft ARPP entries do not lock anything.
  - If ARPP publishes while a revision containing an IPA completion item is pending, approval fails validation and the HoD may reject. The synchronizer is not changed in Phase D. K-4 covers whether it should auto-close such revisions.

### C.9 Phase E compatibility

| Phase D decision | Effect on Phase E |
| --- | --- |
| One actionable Actual revision per project; items carry stage code, kind and before/proposed values | Phase E's Change Set revision can adopt these items as its "Actual fact correction" category, either by re-parenting with a nullable `TimelineChangeSetRevisionId` or by copying. The semantics already match rule 24's project-scoped "latest". |
| Source-neutral final-state validator over a complete projected stage set | Phase E feeds it the combined Plan + Lifecycle + Actual projection. Nothing is rewritten. |
| Shared publisher, project gate and compare-and-set claim | Phase E publication composes the plan, lifecycle and actual publishers inside one gate and transaction. |
| **Not done in D:** cross-category identity, one revision spanning categories, removing per-stage `StageChangeRequest` lineages, merging `PlanVersion` | These are explicitly Phase E. Phase D adds no new cross-category coupling beyond the read-only contradiction check (F.6). |

The interim state has three independent PO sequences per project: plan, lifecycle and Actual. That deviation from rule 24's "one active Change Set" already exists as G05. Phase D narrows the Actual sequence to one per project rather than widening it.

---

## D. Data model and migration impact

### D.1 Why a schema change is genuinely necessary

Phase D needs durable, immutable, project-scoped multi-stage proposals with before/proposed values and decision evidence. Neither `StageChangeLog` nor `StageChangeRequest` can carry this without changing its meaning: the log is an after-the-fact history with a constrained `Action` set, and the request is per stage and status-typed. Section C.1 covers the rejected reuse options.

### D.2 Entities

**`ActualCorrectionRevision`** (`Models/Stages/ActualCorrectionRevision.cs`)

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | int PK | |
| `ProjectId` | int FK → `Projects` | `OnDelete` matches `ProjectStages`. Confirm against the purge path; `ProjectModerationService.PurgeCoreAsync` must also delete these rows. |
| `RevisionNo` | int | Unique index `(ProjectId, RevisionNo)`. |
| `Status` | varchar(16) | CHECK `IN ('Pending','Superseded','Withdrawn','Approved','Rejected')`. |
| `EntryPoint` | varchar(16) | `Editor` or `Backfill`. Presentation and evidence only. |
| `SubmittedByUserId` | varchar(450) | Required. |
| `SubmittedOn` | timestamptz | Required. The epoch. |
| `Note` | varchar(1024) | Optional PO justification (K-9). |
| `SupersedesRevisionId` / `SupersededByRevisionId` | int? self-FK | Lineage evidence. |
| `ClosedByUserId`, `ClosedOn`, `DecisionNote` | | Set on Superseded, Withdrawn, Approved or Rejected. |

- A filtered unique index allows one `Pending` per project: `ux_actualcorrectionrevisions_pending` on `(ProjectId) WHERE "Status" = 'Pending'`. It follows the existing provider-conditional pattern used for `ux_stagechangerequests_pending`.
- A `ContentHash` (SHA-256 of the canonical items) is optional and used for the "Unchanged" check. It can be computed rather than stored; storing it is cheap evidence. Recommendation: store it.

**`ActualCorrectionItem`**

| Column | Type | Notes |
| --- | --- | --- |
| `Id` | int PK | |
| `RevisionId` | FK → revision, cascade restrict | |
| `StageCode` | varchar(16) | Unique `(RevisionId, StageCode)`. |
| `Kind` | varchar(16) | CHECK `IN ('Correction','Backfill')`. |
| `BeforeStatus`, `BeforeActualStart`, `BeforeCompletedOn`, `BeforeRequiresBackfill` | | Official snapshot at submission, used for the baseline check and evidence. |
| `ProposedActualStart`, `ProposedCompletedOn` | date? | Final proposed values, already normalized. |
| `ClearsActualStart` | bool | Records explicit-clear intent, distinct from "null because unknown". |
| `Note` | varchar(1024)? | |

No changes are made to `ProjectStages`, `StageChangeLogs` or `StageChangeRequests`. **`CK_StageChangeLogs_Action` is not altered**, because publication reuses the existing actions `ActualsUpdated` and `Backfill`.

### D.3 Migration

- **Name:** `AddActualCorrectionProposals`, created with `./tools/Add-PrismMigration.ps1 -Name AddActualCorrectionProposals -Context ApplicationDbContext`. This appends `Migrations/immutable-migration-ids.txt` after `20261217000002_VersionPlanningInputAuthority`.
- **Content:** purely additive. Two tables, their indexes and CHECK constraints. No data backfill, since no legacy proposals exist.
- **Tests:** add an assertion to `ApplicationDatabaseMigrationsTests` (model/manifest), and a check in `PostgresMigrationIntegrationTests` that the tables, filtered index and constraints exist after a clean migration.

### D.4 Legacy-data compatibility

- Existing official rows are not touched.
- Pre-existing chronology violations in untouched data do not block unrelated corrections (F.4).
- Rows with `Completed`, `CompletedOn == null`, `RequiresBackfill = true` keep today's display. Facts-only backfill (`RequiresBackfill = true` with a date present, set by `StageProgressService.AutoCompleteStageAndDependenciesAsync` when facts are missing) remains governed by `ProjectFactsService`. That is out of Phase D scope (K-10).

### D.5 Offline deployment and rollback (detail in section I)

- The migration is additive; tables are unused until D-3.
- The startup gate refuses older binaries after the migration. Rolling back D-2 therefore requires the database backup or a reviewed reverse script.
- Rolling back D-3 is a binary swap to D-2. It is *governance-safe* only because R-D2 already refuses PO direct publication (C.7). Section I distinguishes technical, governance and emergency-recovery compatibility.

---

## E. Transaction and concurrency design

### E.1 Boundary

Every Phase D authoritative or proposal-state write runs as one `RelationalTransactionScope` with this sequence:

1. `LockProjectAsync(projectId)`
2. Row-lock the project's stages (approval and direct saves only)
3. Read, including authorization re-read and the editor-baseline check (E.4)
4. Validate
5. Compare-and-set
6. Write the state change **and its required evidence**
7. `SaveChanges`, then `CommitAsync`
8. Post-commit callbacks registered with `RegisterAfterCommit`: secondary `AuditLogs` entry and notifications

Validation always happens **after** the gate and the row lock, never before.

**Transactional evidence contract.** Phase D separates two kinds of record.

1. **Required durable evidence: atomic with the change.** It is written in step 6 on the same `ApplicationDbContext` and the same transaction. Any failure (exception, constraint violation, cancellation) rolls the entire unit back. There is no state change without its evidence, and no evidence of a change that did not happen.

| Action | State change | Required evidence (same transaction) |
| --- | --- | --- |
| Submit | new `Pending` revision | the revision row (submitter, `SubmittedOn`, note, `SupersedesRevisionId`) and its `ActualCorrectionItems` with `Before*` and proposed values |
| Supersede (part of submit) | previous `Pending` becomes `Superseded` | closure fields on the previous row (`SupersededByRevisionId`, `ClosedByUserId`, `ClosedOn`) |
| Withdraw | `Pending` becomes `Withdrawn` | closure fields and the optional reason |
| Reject | `Pending` becomes `Rejected` | closure fields and `DecisionNote` |
| Approve | `Pending` becomes `Approved`, **and** official `ProjectStage` dates and flags | closure fields and `DecisionNote`, plus one `StageChangeLog` per published stage (before/after values, approver, `At`, note naming the revision) |
| Direct HoD/Admin save | official `ProjectStage` dates and flags | one `StageChangeLog` per changed stage, as today |

Together these reconstruct everything rule 26 requires:

- before values (`Before*` and `StageChangeLog.From*`);
- proposed values (items);
- revision identity and lineage;
- submitter and submission time;
- superseded revisions;
- reviewer, decision and decision time;
- the values actually published (`StageChangeLog.To*`).

Acceptance criterion J-2 requires the items, the log rows and `ProjectStage` to agree.

2. **Secondary records and side effects: only after a successful commit.**

- The `AuditLogs` entries (`Projects.ActualCorrectionSubmitted`, `…Withdrawn`, `…Approved`, `…Rejected`, `Projects.ActualsUpdated` and `Projects.StageBackfilled`).
- Notifications through `INotificationPublisher`.

Both run as `RegisterAfterCommit` callbacks. A failure is collected in `PostCommitErrors` and logged at error level, and does not reverse or misreport the committed action. If the transaction rolls back, the callbacks are discarded, so no notification announces a change that did not happen.

**Why `AuditLogs` is not written inside the transaction:**

- *It is not the record of the change.* `AuditLogs` is the cross-cutting security and usage log. Domain history (`StageChangeLog`, revision rows) is the authoritative timeline record, the same split as `PlanApprovalLog` in the plan services.
- *It is the codebase's established post-commit pattern.* ARPP services register `AuditLogs` with `RegisterAfterCommit` (B.4).
- *Calling it in-transaction is unsafe on PostgreSQL.* `AuditService.LogAsync` runs `PersistDailyActionSummaryAsync` as a raw-SQL upsert and swallows its failure. Raw SQL is not wrapped in a savepoint, so inside an outer PostgreSQL transaction a swallowed failure leaves the transaction aborted. The outcome of the commit would then depend on a telemetry write. Phase D does not rely on how the driver handles `COMMIT` in an aborted transaction; the existing in-transaction callers are recorded in B.6 #7.

This needs no outbox or event infrastructure. The existing `RelationalTransactionScope` covers both kinds of record.

**Ordering details that matter:**

- `ActualFactPublisher.Apply` only stages tracked changes. It never calls `SaveChanges` or any service that does, such as `IAuditService`, `INotificationPublisher` or `StageProgressService`. Nothing inside the unit can flush or commit early.
- `StageChangeLog` rows reuse allowed `Action` values (`ActualsUpdated`, `Backfill`), and their notes are sized to the 1024-character column. A constraint violation therefore indicates a defect, and it rolls back the unit rather than being silently skipped.

### E.2 Gate reuse

Reuse `PlanRevisionConsistency.LockProjectAsync`. It is `internal` in `ProjectManagement.Services.Plans` and already called from `Services.Stages`, so it is in the same assembly. Do not add a second lock key: a shared key deliberately serializes Actual publication with plan publication, `StageDirectApplyService` and `StageDecisionService` approval.

### E.3 Row lock for writers outside the gate

`ArppIpaStageSynchronizer`, `HistoricalStageRecordService`, `ProjectFactsService` and `Create` do not take the gate.

- On PostgreSQL, approval and direct saves load the project's stages with `FromSqlInterpolated($"SELECT * FROM \"ProjectStages\" WHERE \"ProjectId\" = {projectId} FOR UPDATE")`.
- Under READ COMMITTED, a concurrent synchronizer `UPDATE` either:
  - blocks until we commit, after which ARPP correctly overwrites IPA; or
  - has already committed, in which case we read its values, and the subsequent `ResolveAsync` sees the published entry.
- On SQLite and other test providers, the gate's write lock is enough.

Do **not** make the synchronizer take the gate. Its multi-project batches would create lock-ordering and deadlock risk.

### E.4 Stale-edit and stale-decision guards

**Decision side (HoD):**

1. Revision `Status = 'Pending'` with exact `SubmittedOn`, checked inside the gate and enforced again by the compare-and-set claim.
2. Per-item baseline equality against locked official rows.
3. Final-state revalidation against current official facts, the current IST date, current ARPP authority and the current workflow and PNC.
4. Self-approval refusal.

`StageDecisionService.DecideAsync` should also run its existing `StageValidationService` call **after** `LockProjectAsync`. This is a small move, scheduled in D-1, with a regression test for a concurrent Actual publication.

**Editor side (PO submission and HoD/Admin direct save): the editor baseline.**

The server issues the baseline with every rendering of the Actual editor (`ProjectTimelineReadService.GetActualsEditorAsync`) and the backfill modal (`OverviewModel.BuildBackfillViewModel`). It has three parts:

| Part | Value | Purpose |
| --- | --- | --- |
| `BaselineVersion` | constant `1` | Marks a post as coming from a baseline-aware form. Its absence identifies legacy or incomplete posts. |
| Actionable-revision identity | `BasedOnRevisionId` and `BasedOnSubmittedOn` of the project's current `Pending` revision, or both empty for **"none"** | Detects any change in the actionable revision, including "a revision now exists where none did". |
| Per-stage `StageToken` | for every row rendered: SHA-256 hex of the canonical official `StageCode`, `Status`, `ActualStart`, `CompletedOn` and `RequiresBackfill` | Detects a change to the official facts of a stage the post touches. |

About the token:

- It is computed by one small static helper, `ActualStageToken.Compute(ProjectStage)`, modelled on `PlanEditFingerprint`.
- It is not an authorization or CSRF token. Antiforgery stays as it is, and authorization is re-read from the project row inside the gate (C.2).
- No new table or column is needed: the server recomputes the expected values from the locked rows and the revision table.

**Validation semantics, inside the gate, before any write:**

- **Marker absent.**
  - A PO proposal returns **Stale** ("This page is out of date. Reload…"). It is never silently treated as "none".
  - A direct HoD/Admin save keeps legacy behaviour in R-D1 only, and the acceptance is logged. From R-D3 it is also **Stale**.
- **Revision identity.** The posted identity must equal the current actionable identity exactly. "None" matches only "none".
- **Stage tokens.** These apply to the *resulting item set*: changed rows from the editor, and submitted plus carried-forward rows from the backfill entry. For each such stage, the posted token must equal the token of the locked official row. A carried-forward item has no posted token, so its `Before*` values are compared with the locked row instead. Stages outside the item set are not compared: rule 20 lets non-overlapping changes to different stages both succeed. Their current values still feed the final-state validation.
- Any mismatch returns **Stale** and writes nothing: no revision, no supersession, no official change. The PO reloads and continues; nothing is locked (C2 principle).

**Required scenarios.** These are tests in G and H.

| # | Scenario | Outcome |
| --- | --- | --- |
| S1 | Tabs A and B open with no pending revision (both "none"). A submits | A creates revision 1 (`Pending`) |
| S2 | B then submits its stale content | B posted "none" but revision 1 exists, so **Stale**. Revision 1 is untouched and stays actionable. The filtered unique index is the backstop if the check were bypassed |
| S3 | PO opens the form; a HoD's direct save (or a lifecycle decision, or ARPP sync) changes stage X; PO submits a change to X | X's token differs, so **Stale**, naming X |
| S3b | Same, but the PO's change is to an unrelated stage Y | Accepted. X's new official values are used in final-state validation, and Y's `Before*` comes from the locked row |
| S4 | Revision N is approved, rejected, withdrawn or superseded while an older form based on N is open; the old form submits | Posted N, but the current identity is "none" or N+1, so **Stale**. A decided N is never resurrected or superseded |
| S5 | Lead PO reassigned while the old PO's form is open | Old PO's submit or withdraw is **Forbidden** (re-read assignment). The new PO's fresh form submits normally and may supersede the pending revision (K-6) |
| S6 | Legacy or incomplete post (no marker, missing identity fields, or a missing token for a stage in the item set) | PO: **Stale**, no write. Direct: legacy behaviour in R-D1 only, logged; **Stale** from R-D3 |

The server-issued baseline is the whole versioning mechanism. It reuses existing fields and the C2 fingerprint idea, and adds no Draft state, no lock and no generic versioning framework.

### E.5 Race scenarios (each becomes a test; "PG" means real PostgreSQL with two connections)

| # | Race | Expected outcome | Test |
| --- | --- | --- | --- |
| R1 | Two HoDs approve revision N | One publishes; the other gets AlreadyDecided with no second log | PG |
| R2 | HoD approves N while PO submits N+1 | Serialized by the gate. If the PO wins, N is superseded and the approval is Stale. If the HoD wins, N is published and the PO's submit (based on N) is Stale | PG |
| R3 | HoD rejects N while PO withdraws N | Exactly one terminal state | PG |
| R4 | Approval vs HoD direct correction of the same stage | Second actor: approval gets SourceChanged, or the direct save is stale (token) | PG |
| R5 | Approval vs ARPP verify publishing IPA | ARPP wins, either way (E.3). An IPA completion item can never overwrite the published date | PG |
| R6 | Approval vs lifecycle decision on the same or a dependent stage | Serialized; the second revalidates against committed state | PG |
| R7 | Approval vs plan approval | Serialized; disjoint columns; both succeed | SQLite plus PG |
| R8 | Two PO tabs submit concurrently, both from a "none" baseline (S1/S2) or both based on N | One wins; the other is Stale (identity mismatch); the filtered unique index is the backstop | PG |
| R9 | Publication fails mid-way (injected failure on a `StageChangeLog` insert, the stage update or the closure update) | Full rollback: no stage change, no log, revision still `Pending`, no `AuditLogs` row, no notification | SQLite plus PG |
| R10 | Double-click approve | Idempotent AlreadyDecided | SQLite |
| R11 | Old and new binary overlap | Not supported. Prevented by the outage SOP (section I) | Operational |
| R12 | HoD direct save of stage X commits while the PO's submit touching X waits on the gate (S3) | PO Stale on X's token; no revision written | PG |
| R13 | Post-commit `AuditLogs` or notification callback throws after a successful approval | Approval stays committed and is reported as success; all required evidence is present; error recorded in `PostCommitErrors` and logged | SQLite |

---

## F. Validation design

### F.1 Component

`IActualTimelineValidationService`, implemented by `Services/Stages/ActualTimelineValidationService.cs` and registered as scoped in `Program.cs` next to `IStageValidationService`.

```csharp
public sealed record ActualStageState(string StageCode, StageStatus Status,
    DateOnly? ActualStart, DateOnly? CompletedOn, bool RequiresBackfill);

public sealed record ActualStageChange(string StageCode, DateOnly? ActualStart,
    bool ClearActualStart, DateOnly? CompletedOn);          // already normalized

public enum ActualValidationSource { Direct, Backfill, ProposalSubmission, Approval }

public sealed record ActualTimelineCandidate(int ProjectId,
    IReadOnlyList<ActualStageState> Official,                // complete, locked
    IReadOnlyList<ActualStageChange> Changes,
    ActualValidationSource Source,
    IReadOnlyList<ProjectedLifecycleState>? PendingLifecycle = null);  // F.6

public sealed record ActualValidationIssue(string StageCode, string Rule,
    string Message, string? RelatedStageCode);

public sealed record ActualTimelineValidationResult(
    IReadOnlyList<ActualValidationIssue> Errors,
    IReadOnlyList<ActualValidationIssue> Warnings,           // pre-existing violations
    IReadOnlyList<string> AffectedStageCodes,                // for review "downstream impact"
    IReadOnlyList<ActualStageState> Final);                  // what would be published
```

The service resolves the workflow (`IProjectStageWorkflowPolicy.GetAsync`), ARPP authority (`IArppIpaStageAuthorityService.ResolveAsync`) and today's IST date (`IClock` plus `IstClock`). Callers on decision paths invoke it **inside** the transaction, after locking.

The intrinsic checks for the status/date matrix and IST limits move here. They are no longer duplicated in `StageActualsUpdateService` and `StageBackfillService`. Lifecycle `StageValidationService` stays separate (transition domain), as B2 did for planned dates.

### F.2 Rules per changed row

1. **Status:** only `InProgress` or `Completed` stages, using the effective status, may change. This preserves current behaviour; `Blocked` is K-7.
2. **`InProgress`:** `ActualStart` required, so it cannot be cleared. A `CompletedOn` is rejected: "Use the stage-transition workflow". The status never changes.
3. **`Completed`:** `ActualStart` is optional and may be explicitly cleared. `CompletedOn`:
   - may be supplied (backfill) or changed;
   - may not be cleared through this path (rule 9; K-5);
   - for a backfill item, is mandatory.
4. **Non-future:** `ActualStart` and `CompletedOn` are on or before today in IST.
5. **Ordering:** `ActualStart` ≤ `CompletedOn` when both exist, including when one side is unchanged. For ARPP-managed IPA, the effective `IssueDate` is the completion.
6. **ARPP:** for a managed IPA, a `CompletedOn` that differs from `IssueDate` is an error. `ActualStart` corrections are allowed.

### F.3 Graph chronology over the final state

For **every** stage X in the final state that has a chronology date, take that date as `ActualStart`. If the start is null and the stage is `Completed`, use `CompletedOn`, as today. Then:

- For each required predecessor in `workflow.RequiredPredecessors(X)`, resolve the branch with the existing traversal: skipped stages are transparent and their ancestry is followed.
- **Each branch that resolves to a dated completion D sets a lower bound: the chronology date must be on or after D.** Same-day is valid.
- Branches that are unresolved (undated `Completed`, or not complete) contribute no bound. They do *not* cancel the bounds of other branches. This is the deliberate change from `StageDateSuggestionResolver`'s suggestion semantics (A.2 item 5).
- Expose the branch resolution as a new resolver method, for example `ResolveBranchBoundaries(...)`. That keeps `Resolve` unchanged for suggestions and the reopen prefill (rule 10).
- Effective IPA state comes from `StageEffectiveState`. The PNC branch follows `workflow.PncApplicable`.
- Skipped stages carry no dates and are never checked as successors.
- The suggested next-day start is never used as a minimum (rule 7).

### F.4 Blocking versus pre-existing violations

Compute the violations of the **official** state and of the **final** state. A final-state violation is an **Error** if any date it depends on differs from its official value. That covers:

- the successor's chronology date;
- the predecessor completion that supplied the bound;
- the start/completion pair on the row.

All other violations are **Warnings**, reported but not blocking. As a result:

- Rule 8 holds: a changed predecessor makes an untouched successor's violation an error that names both stages.
- A coordinated correction of both stages is accepted when the final state is valid.
- Legacy bad data in unrelated rows never blocks an unrelated correction. This preserves the tested behaviour "unchanged invalid rows not repaired".
- The same rules apply to every source (`Direct`, `Backfill`, `ProposalSubmission`, `Approval`). Only authorization differs.

Messages name the stage, the rule, the date and the controlling predecessor. Example: "SOW: Actual start 11 Jan 2026 is before IPA Actual completion 20 Jan 2026. Same-day commencement is permitted. Correct SOW in the same update."

### F.5 Mutation-boundary matrix

This matrix covers every authoritative writer found (B.1), as it will stand after Phase D. "Enforced" means the shared validator's errors block the write; "warning-only" means violations are logged as `Stages.ActualChronologyWarning` and the write proceeds.

| # | Boundary | Writes Actual facts? | Shared validator | Mode | Authorization | Transaction / concurrency | Justification for any deferral |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `StageActualsUpdateService.UpdateAsync` (direct, via `EditActuals`) | `ActualStart`, `CompletedOn`, `RequiresBackfill`, auto flags; `Status` only for ARPP-effective IPA | yes (`Direct`), from D-1 | **enforced** | Admin or any HoD. PO-only users are cut off from R-D2 (C.7) | D-1: tx, gate, `FOR UPDATE`, `StageToken` | — |
| 2 | `StageBackfillService.ApplyAsync` (direct, via `BackfillApply`) | `ActualStart`, `CompletedOn`, `RequiresBackfill = false`, auto flags | yes (`Backfill`), from D-1 | **enforced** | Same as #1 | Same as #1 | — |
| 3 | `ActualCorrectionProposalService.SubmitAsync` | none (proposal tables only) | yes (`ProposalSubmission`) plus the F.6 lifecycle check | **enforced** | Current `LeadPoUserId`, PO-only | tx, gate, editor baseline (E.4) | — |
| 4 | `ActualCorrectionProposalService.ApproveAsync` | same fields as #1 and #2, via `ActualFactPublisher` | yes (`Approval`) | **enforced** | Admin or HoD per K-1; no self-approval | tx, gate, `FOR UPDATE`, epoch compare-and-set, per-item baseline | — |
| 5 | `StageDirectApplyService.ApplyCoreAsync` (HoD lifecycle) | `Status`, `ActualStart`, `CompletedOn`, backfill and auto flags; forced predecessor backfill | yes, observability (D-1d) | **warning-only** | `HoD` role (`ApplyChangeModel`) | tx and gate (existing) | A lifecycle transition (reopen, skip, forced completion, completion without date) is not an Actual correction. Blocking it on downstream chronology changes lifecycle semantics: rules 10/18 interplay and convergence policy, which belong to Phase E. Its own `StageValidationService` check of the stage's predecessor boundary stays enforced. |
| 6 | `StageDecisionService.DecideAsync` → `StageProgressService.UpdateStageStatusAsync` (approved lifecycle request) | same as #5, plus the completion cascade | yes, observability (D-1d) | **warning-only** | Admin or HoD (`ApprovalAuthorization`) | tx, gate, compare-and-set claim; validation moved after the gate in D-1c | Same as #5 |
| 7 | `HistoricalStageRecordService.SaveAsync` (legacy terminal reconstruction) | `Status`, both dates, `RequiresBackfill` | yes, observability (D-1d) | **warning-only** | Admin or HoD; legacy projects that are `Completed`/`Cancelled` only (`EnsureEligible`) | tx, no gate | Evidence-backed reconstruction of closed legacy projects. It keeps its own intrinsic checks (future dates, lifecycle-date bound, ARPP). These projects are excluded from Phase D proposals (C.4 step 2), so the paths cannot interact except through a stale approval, which the per-item baseline blocks. |
| 8 | `ArppIpaStageSynchronizer` (ARPP verify/reconcile, startup) | IPA `Status`, `CompletedOn`, flags | no | **exempt** | System (`VerifyArpp`, `ManageArpp` policies upstream) | caller's tx, no gate | Rule 17 makes the published ARPP date authoritative even when it conflicts with chronology; conflicts are surfaced as data-quality audit entries. Validating would contradict the rule. Phase D approval is protected by the row lock (E.3). |
| 9 | `ProjectFactsService.ClearBackfillFlagAsync` (via `Procurement/Edit`) | `RequiresBackfill = false` only, and only when the stage's dates are already present | n/a (no dates written) | n/a | Admin, HoD, assigned PO, after hariomahlawat/ProjectManagement#2673 | page-level tx | Cannot hide a missing completion date (it returns early when `IsMissingRequiredDates`). Containment: **merge #2673 before R-D3**, so that a PO-only user cannot affect another project's backfill state. |
| 10 | `CreateModel.OnPostAsync` (project creation) | initial `Completed` stage with `ActualStart = CompletedOn` | no | n/a | `Project.Create` (Admin, HoD) | none | Runs only when the project is created, so no successors or proposals exist. The invented start deviates from rule 3 (B.6 #8); that is a separate defect, not Phase D. |
| 11 | `PlanApprovalService` / `EditPlan` stage materialization | no (creates `NotStarted` rows with planned dates) | n/a | n/a | existing | existing gate | — |
| 12 | `ProjectModerationService.PurgeCoreAsync` | deletes stage rows | n/a | n/a | `AdminPolicies.RecoveryManage`, retention worker | existing | D-2 adds deletion of revision and item rows (G). |

**What Phase D guarantees, and what it does not:**

- **Guaranteed.** No Actual-date correction or completion-backfill publication (rows 1, 2 and 4) commits a new final-state chronology violation (F.4), or any intrinsic, status or ARPP rule violation. No PO submission (row 3) records a proposal that would do so against the official facts at submission time.
- **Not guaranteed.** Lifecycle writers (rows 5–7) can still commit chronology violations, as today; Phase D makes them visible as warnings. ARPP (row 8) is deliberately authoritative.
- **Why the excluded writers do not undermine the guarantee:**
  - Any of them that changes a stage covered by a pending revision changes that stage's `StageToken` and the item's baseline. The revision then becomes non-approvable (`SourceChanged`) instead of publishing over the change.
  - Approval revalidates against whatever they committed.
  - The only additional containment needed is #2673 (row 9).

### F.6 Pending-lifecycle contradiction check (proposal submission only)

Build the projected state as official plus the latest pending `StageChangeRequest` per stage. Reuse the projection semantics of `StageRequestService.BuildProjectedStates`/`ApplyProjection`; extract them into an internal static helper rather than duplicating them.

Then apply the Actual changes, and run F.2–F.4 on that projection as well.

- An error that exists only in the projection is reported as: "Conflicts with the pending {stage} lifecycle update awaiting HoD approval. Revise or include it." The submission is not saved.
- If a pending lifecycle request on a stage would make that stage's correction meaningless, the item is also rejected with that message. Examples: a reopen to `NotStarted` while a start correction is proposed, or a skip.
- Approval does **not** re-run against pending lifecycle state. Only official facts can publish. The baseline check covers lifecycle approvals that land first.

---

## G. File-by-file implementation sequence

Each step is one reviewable PR. Durable documentation is updated in the same PR. The release grouping is in section I.

### D-1a — Shared Actual validation and publication (no schema)

| Files | Change |
| --- | --- |
| **New** `Services/Stages/ActualTimelineValidationService.cs` | F.1–F.4 |
| **New** `Services/Stages/ActualChangeNormalizer.cs` | Blank means preserve; explicit clear; managed IPA completion projection (logic lifted from `StageActualsUpdateService.UpdateAsync`, lines ~107–188) |
| **New** `Services/Stages/ActualFactPublisher.cs` | Field effects plus `StageChangeLog` (lifted from lines ~293–332) |
| `Services/Stages/StageDateSuggestionResolver.cs` | Add `ResolveBranchBoundaries`; `Resolve` unchanged |
| `Program.cs` | Register `IActualTimelineValidationService` |

Tests (new `ActualTimelineValidationServiceTests.cs`, SQLite):

- V1/V2 topology; convergence COB←TEC+BM with a dated and an undated branch; the EAS←PNC branch on/off.
- Skipped ancestry; same-day start; next-day suggestion not enforced.
- Changed predecessor invalidating an untouched successor; a coordinated two-stage fix passes.
- Pre-existing violation reported as a warning only.
- Managed IPA start versus completion; IST midnight boundary via `FakeClock.ForIst`.

### D-1b — Rewire direct paths

| Files | Change |
| --- | --- |
| `Services/Stages/StageActualsUpdateService.cs` | Transaction, gate, `FOR UPDATE` load, normalizer, validator (`Direct`), publisher. Keep the pending-request conflict for direct saves. `StageChangeLog` rows in the transaction. `Projects.ActualsUpdated` moves to `RegisterAfterCommit` (E.1). `StageToken` check (E.4) |
| `Services/Stages/StageBackfillService.cs` | Same, with source `Backfill`. Keep 409 conflict semantics. New post-commit audit `Projects.StageBackfilled` |
| **New** `Services/Stages/ActualStageToken.cs` | `Compute(ProjectStage)`, the canonical SHA-256 of `StageCode`, `Status`, `ActualStart`, `CompletedOn` and `RequiresBackfill` (E.4) |
| `ViewModels/ActualsEditorVm.cs`, `Services/Projects/ProjectTimelineReadService.cs` (`GetActualsEditorAsync`), `Pages/Projects/Overview.cshtml.cs` (`BuildBackfillViewModel`), `Pages/Projects/Timeline/_EditActualsForm.cshtml`, `Pages/Projects/_BackfillForm.cshtml`, `wwwroot/js/projects/overview.js` | Emit `BaselineVersion`, the actionable-revision identity ("none" until D-3) and a per-row `StageToken`; post them back (additive fields) |
| `Pages/Projects/Timeline/EditActuals.cshtml.cs` | Exclude `IsDeleted` projects. Map a new stale result to a message |

Tests:

- Update `StageActualsUpdateServiceTests` and `StageBackfillServiceTests`: graph errors now raised for backfill; untouched successor.
- **Failure injection** with a SQLite `SaveChangesInterceptor` that throws when a `StageChangeLog` is added:
  - no `ProjectStage` change persists;
  - no `AuditLogs` row is written;
  - the caller gets an error, not "updated successfully".
- **Post-commit failure:** a throwing `IAuditService` fake leaves the correction and its `StageChangeLog` committed, and the error is logged.
- Stale token rejection (S3); unrelated-stage change accepted (S3b); legacy no-marker post accepted and logged (S6, R-D1 only).
- Existing `EditActualsAuthorizationTests` must still pass unchanged in D-1, because authority is not altered yet.
- ARPP tests in `ProjectManagement.Tests/Arpp/ArppIpaStageSynchronizerTests.cs` (`ActualsEdit_*`, `CompletionBackfill_*`) unchanged.

### D-1c — Decision TOCTOU fix

| Files | Change |
| --- | --- |
| `Services/Stages/StageDecisionService.cs` | Move `LockProjectAsync` and the transaction above the validation block. Behaviour is otherwise identical |

Tests: SQLite ordering test, plus a PG scenario in a new `StageActualsConcurrencyPostgresFixture` (see H).

### D-1d — Lifecycle observability (recommended)

| Files | Change |
| --- | --- |
| `StageDirectApplyService.ApplyCoreAsync`, `StageDecisionService.DecideAsync`, `HistoricalStageRecordService.SaveAsync` | After mutation and before commit, run the validator in warn mode over the tracked final state. Write the structured log warning `Stages.ActualChronologyWarning` through `ILogger`, not `IAuditService` (E.1). Never block (F.5 rows 5–7) |

Tests: the warning is emitted; the outcome and persisted state are unchanged; a validator exception is caught and logged without affecting the lifecycle write.

### D-1e — Documentation

Update `docs/timeline.md` ("Actual-date editing and backfill"), the `docs/timeline-proposal-roadmap.md` G12/G13 status (closed for direct paths), `docs/data-domain.md` (stale backfill and lock text), and this plan's status.

### D-2 — Schema (unused) and PO direct-publication cut-off

| Files | Change |
| --- | --- |
| **New** `Models/Stages/ActualCorrectionRevision.cs`, `Models/Stages/ActualCorrectionItem.cs` | Entities (D.2) |
| `Data/ApplicationDbContext.cs` | `DbSet`s and configuration, following the `StageChangeRequest` provider-conditional filter/check pattern |
| **New** migration via `tools/Add-PrismMigration.ps1`; `Migrations/immutable-migration-ids.txt` | Additive |
| `Services/Projects/ProjectModerationService.cs` (purge) | Delete revisions and items with the project |
| `Infrastructure/ApplicationDatabaseSchemaValidator.cs` | Validate the new tables, if this validator enumerates critical tables |
| `Pages/Projects/Timeline/EditActuals.cshtml.cs`, `Pages/Projects/Stages/BackfillApply.cshtml.cs` | **PO cut-off** (C.7). A PO-only user's post is refused before any service call: a redirect with an error flash for `EditActuals`, or `403 { ok:false, error:"approval-required" }` for `BackfillApply`. Admin/HoD unchanged |
| `Pages/Projects/Timeline/_EditActualsForm.cshtml`, `Pages/Projects/_BackfillModal.cshtml`, `wwwroot/js/projects/overview.js` | For PO-only users, render the editor read-only with "Ask the HoD to apply Actual-date corrections", and handle `approval-required`. Visibility only; the page check is the boundary |

Tests:

- `ApplicationDatabaseMigrationsTests`, the `PostgresMigrationIntegrationTests` physical-schema assertions, and a purge test.
- **Authority:** rewrite the PO cases of `EditActualsAuthorizationTests`: `OnPostAsync_PreservesCurrentAuthority`, `OnPostAsync_IpaCompletionCorrectionUsesConditionalPublishedAuthority` and `OnPostAsync_IpaStartRemainsSeparatelyCorrectableUnderNormalRoleAuthority`. An assigned PO is now refused, and `ProjectStage` and `StageChangeLog` are unchanged. Admin and HoD stay unchanged.
- **Backfill authority:** add `StageBackfillAuthorizationTests` cases: assigned PO gets `approval-required`; HoD and Admin apply directly.
- **Roles:** a user with both PO and HoD roles keeps direct authority.

### D-3a — Proposal service

| Files | Change |
| --- | --- |
| **New** `Services/Stages/ActualCorrectionProposalService.cs` | `SubmitAsync`, `WithdrawAsync`, `ApproveAsync`, `RejectAsync`, `GetActionableAsync` (C.4–C.6). Result types are a discriminated record (`Success`, `Unchanged`, `Stale`, `SourceChanged`, `Validation`, `Conflict`, `Forbidden`, `NotFound`) |
| **New** `Services/Stages/ProjectedLifecycleState.cs` | Extracted from `StageRequestService` private types. `StageRequestService` is refactored to use it with no behaviour change, proven by its existing 18 tests |
| `Services/Stages/IStageNotificationService.cs` or a new `IActualCorrectionNotificationService` | After-commit notifications (K-8). Optional |

Tests (SQLite, plus PG in `ActualCorrectionConcurrencyPostgresFixture`, wired into `PostgresMigrationIntegrationTests.CompleteStartupBoundary_MigratesAndValidatesBothContexts`, R1–R13):

- Submit writes no `ProjectStage` values.
- Supersede, withdraw, unchanged, empty, backfill carry-forward, kind derivation.
- Approval publishes the exact values; baseline change gives `SourceChanged`; ARPP published mid-flight.
- Self-approval; reject without validity check (but with the epoch check); immutability of items.
- **Editor baseline, service level, S1–S6:** first submission from "none"; stale "none" after a revision exists; stale token on a touched stage; unrelated-stage change accepted; old form after approve, reject, withdraw or supersede; reassigned PO forbidden; missing marker refused with no write.
- **Failure injection, SQLite with a PG variant for approve.** Use a `SaveChangesInterceptor` or command interceptor to make one write fail:

  | Action | Write that fails | Asserted result |
  | --- | --- | --- |
  | Approve | the `StageChangeLog` insert, the `ProjectStage` update, or the revision closure update | revision still `Pending`; no stage or log change; no `AuditLogs` row; no notification; result is not `Success` |
  | Submit | the item insert | previous revision still `Pending`; no new revision |
  | Withdraw / Reject | the closure update | status unchanged |

- **Post-commit:** a throwing `IAuditService` or notification fake after approval. Approval committed, all required evidence present, `PostCommitErrors` populated, error logged (R13).

### D-3b — PO routing on existing routes

| Files | Change |
| --- | --- |
| `Pages/Projects/Timeline/EditActuals.cshtml.cs` | Replace the R-D2 PO refusal with a call to `SubmitAsync`, passing the posted editor baseline. Set a flash message ("Sent to HoD for approval. You can revise it at any time until it is approved."). Map `Stale` to a reload message. Admin/HoD keep the direct path, now with the marker required (E.4). Same route and form names |
| `Pages/Projects/Stages/BackfillApply.cshtml.cs` | Same routing. JSON gains `pendingApproval: true, revisionNo` (additive; `ok` stays `true`); `Stale` maps to `409 { ok:false, error:"stale" }` |
| **New** handler `EditActuals?handler=Withdraw` | Exact id plus epoch |
| `wwwroot/js/projects/overview.js` (backfill submit), `wwwroot/js/projects/actuals-edit.js` | Handle `pendingApproval` and `stale`. Optionally consume server-emitted predecessor lists for graph-correct client hints (G11); the server stays authoritative |

Tests:

- Update the PO cases in `EditActualsAuthorizationTests` again. The assigned PO now creates a revision, and official values and `StageChangeLog` stay unchanged. HoD/Admin are unchanged.
- `StageBackfillAuthorizationTests`: assigned PO gets a proposal; HoD applies directly.
- **Page-level baseline tests** for `EditActuals` and `BackfillApply`:
  - a post without the marker gives Stale and no write (PO and direct);
  - two posts from "none" (the second is Stale);
  - a post from a decided revision gives Stale;
  - a reassigned PO gets Forbid.
- JS tests in `actuals-edit-*.test.js`.

### D-3c — Read models and UI copy

| Files | Change |
| --- | --- |
| `Services/Projects/ProjectTimelineReadService.cs` (`GetActualsEditorAsync`, `GetAsync`) | Add `PendingActualCorrection` (revision number, submitted time, items with official and proposed values) |
| `ViewModels/ActualsEditorVm.cs` | Additive proposed-value fields |
| `Pages/Projects/Timeline/_EditActualsForm.cshtml`, `Pages/Shared/_ProjectTimeline.cshtml`, `Pages/Projects/_BackfillModal.cshtml` | Show "Proposed … awaiting HoD approval" separately from official values. PO prefill from the pending revision. "Needs revision: official facts changed" state. Withdraw button. Follow the no-inline-script lint |

Tests: render tests in `ProjectTimelineUiTests` and `ProjectOverviewLifecycleTests`: the official value is never replaced by a proposed one; a viewer sees no actions.

### D-3d — Decision Centre

| Files | Change |
| --- | --- |
| `ViewModels/ApprovalQueueViewModels.cs` | Add `ApprovalQueueType.ActualCorrection` (append-only enum value) |
| `Services/Approvals/ApprovalQueueService.cs` | Queue rows and a detail view: revision, submitter, time, items grouped as "Actual corrections" and "Completion date backfill", current versus proposed, live `AffectedStageCodes`, and stale or source-changed badges |
| `Services/Approvals/ApprovalDecisionService.cs` | Route to `ApproveAsync`/`RejectAsync` with `request.SubmittedOn`, mapping outcomes to the existing `ApprovalDecisionResult` |
| `Pages/Approvals/Pending/*` | Detail partial |

Tests: queue, detail, and decide with a stale epoch; the reviewed values equal the published values equal the logged values.

### D-3e — Documentation closure

Update `docs/timeline.md` rules-to-implementation notes, the roadmap (G03/G04 closed, G11 partial), `docs/phase-d-handoff.md` (status: implemented), the `docs/user-guide` timeline page and the `docs/manual-tests` script.

---

## H. Regression and integration test matrix

| Area | Scenarios | Level |
| --- | --- | --- |
| No premature mutation | PO submit, resubmit, withdraw, reject: `ProjectStages` byte-identical and no `StageChangeLog` | SQLite |
| Authorization | Assigned PO gets a proposal (refused in R-D2); unassigned PO is forbidden; Viewer is forbidden; HoD/Admin go direct; PO+HoD user goes direct; self-approval refused; Admin decide per K-1; soft-deleted project gives NotFound; reassigned PO forbidden at submit and withdraw | Page plus service |
| Status/date matrix | InProgress start required; no completion on InProgress; Completed start clearable; completion not clearable; future dates (IST 23:59/00:01); start > completion | Unit |
| Graph | V1 vs V2 ordering; skipped ancestry; convergence (dated plus undated branch); PNC applicable on/off; same-day; next-day not enforced | Unit |
| Final state | Predecessor change breaks an untouched successor (error); coordinated fix (pass); pre-existing violation (warning) | Unit plus service |
| Backfill | Kind derivation; carry-forward with pending corrections; carried item with changed baseline gives Stale; 409 when no longer needed; auto-completed stage; facts-only flag untouched | Service |
| ARPP | None, draft or published × IPA start or completion; publication between submit and approve; startup sync | Service plus PG (R5) |
| Lifecycle interplay | Pending reopen, skip or completion contradiction at submit; lifecycle approval first then actual approval (SourceChanged); actual approval first then lifecycle decision revalidates; D-1d warning emitted without changing the outcome | Service |
| Rejection / withdrawal / supersession | Stale epoch on approve **and** reject; superseded N not decidable; withdraw by a non-owner refused | Service |
| **Editor baseline (E.4)** | S1–S6 at service level and page level (`EditActuals`, `BackfillApply`) | Service plus page |
| **Evidence atomicity (E.1)** | Failure injection on each required-evidence write for approve, submit, withdraw, reject and direct save: no state change, no evidence, no `AuditLogs`, no notification, non-success result | SQLite, plus PG for approve |
| **Post-commit failure** | Throwing audit or notification fake: action committed, evidence complete, error logged (R13) | SQLite |
| Concurrency | R1–R8 and R12 with two `ApplicationDbContext` connections and a held lock, following `PlanRevisionConcurrencyPostgresFixture` | **PostgreSQL** |
| Recovery | R-D3 → R-D2 smoke script (I.3) on a staging copy: PO refused, HoD direct works, pending revisions inert; re-upgrade marks changed revisions `SourceChanged` | Manual (`docs/manual-tests`) |
| Regression (unchanged) | All B1/B2/C1/C2 suites; `StageRequestServiceTests`; `StageDirectApplyServiceTests`; ARPP suites | Existing |
| Front-end | `npm test` (actuals JS), `npm run lint:views` | CI |

Green SQLite tests do not prove relational locking or real rollback. R1–R8, R12 and the PostgreSQL approve failure-injection case must run in the `.github/workflows/database-migrations.yml` PostgreSQL job before R-D3 is released.

---

## I. Deployment, compatibility and recovery

Production state is **unverified**. Before any release, operators must establish:

- the deployed binary and migration head, using `ops/database/production-migration-inventory.sql`;
- the number of IIS sites, app pools and worker processes that can write (K-12).

The authority model is fixed in code: `[Authorize(Roles = …)]` attributes and `LeadPoUserId` comparisons in the page models. There is **no configuration switch, feature flag or maintenance mode** for Actual-date editing; searches of `Program.cs`, the policy registrations and `appsettings*.json` found none. Recovery therefore relies only on:

- the binaries themselves;
- `app_offline.htm`;
- database backup and restore (`ops/backup-db.ps1`, `ops/restore-db.ps1`);
- the SOP in `docs/deployment/offline-ws2022.md` §D.

This plan adds no switch. A switch would create a second authority mode that could drift from the business rule.

### I.1 Release plan

| Release | Contents | DB change | Authority for PO-only users |
| --- | --- | --- | --- |
| R-D1 | D-1a … D-1e | none | Unchanged (direct publication, now validated). This is today's production behaviour. |
| R-D2 | D-2: unused tables **plus the PO cut-off** | additive migration | **No direct publication.** Refused with "ask the HoD" (C.7). |
| R-D3 | D-3a … D-3e, released together | none | Proposals for HoD approval. |

**Recommended deployment:** R-D2 and R-D3 in **one maintenance window**. Apply R-D2 (migration plus cut-off), smoke-test it, then deploy R-D3 straight away. POs then never experience the R-D2 interruption in normal operation, and R-D2 exists as a tested emergency floor. If the owner instead wants R-D2 to run on its own for a period, POs cannot correct Actual dates themselves during that time; HoD/Admin can (K-14).

### I.2 Procedure for each release (offline SOP §D)

1. Back up the database.
2. Place `app_offline.htm` and stop **every** process that can write: IIS app pools, plus any other instance. Mixed old/new writers are unsupported. An older binary would let a PO publish directly while the new one expects proposals.
3. R-D2 only: the startup gate applies the migration under its advisory lock. Generate `migration.sql` (`dotnet ef migrations script -i`) beforehand for review and the release note. Resolve discrepancy B.6 #1 with the operators beforehand (K-13).
4. Deploy the complete publish folder; run `--compendium-offline-self-test`; check `/health`.
5. Smoke test:
   - R-D1: HoD direct correction, plus a chronology rejection message.
   - R-D2: an assigned PO's post is refused, and `ProjectStage` is unchanged. A HoD direct correction succeeds.
   - R-D3: PO submits; HoD sees it in the Decision Centre; approve publishes; reject leaves official values unchanged; a second tab's stale submit is refused.
6. Tell users to reload open Overview pages. Forms rendered before R-D3 lack the baseline marker, so a PO post from one is refused as stale, never silently accepted (E.4 S6).

### I.3 Rollback and emergency recovery

The plan distinguishes three questions for each rollback target.

| Target | Technical compatibility | Governance compatibility | Use |
| --- | --- | --- | --- |
| R-D1 → previous release | Binary swap; no schema change | Restores pre-D validation only; PO authority is the same as R-D1 (today's behaviour) | Normal rollback for D-1 defects |
| R-D3 → R-D2 | Binary swap; same schema and migration head | **Safe.** R-D2 refuses PO direct publication, so the Phase D authority boundary holds. Proposals become unavailable; HoD/Admin direct correction continues | Emergency floor only, when R-D3 cannot run safely |
| R-D2 (or R-D3) → R-D1 or earlier | **Blocked** by the startup gate. It needs a database restore from before R-D2, or a reviewed reverse script | **Unsafe.** It reinstates direct PO publication, and restoring the backup discards every change since it was taken | Only for a catastrophic schema defect, by explicit owner decision; not a routine rollback |

**Preferred recovery is a fix-forward to a corrected R-D3** (D-3.1). Use it for defects in proposal UI, read models, the Decision Centre or notifications, and for any defect that does not let approval publish wrong values. Use the R-D2 floor only when:

- approval or submission could corrupt authoritative facts or evidence; or
- the application cannot start or serve on R-D3;

and a fix-forward cannot be produced within the outage window.

**Recovering onto R-D2:**

1. Follow the SOP outage (`app_offline.htm`, stop every writer), restore the R-D2 publish folder from `releases\previous`, and do **not** restore the database. The schema is unchanged.
2. **Verify before reopening:**
   - The deployed binary is R-D2. The migration head equals the R-D2/R-D3 head (`ops/database/production-migration-inventory.sql`).
   - An assigned PO's post to `EditActuals` and to `BackfillApply` is refused, and `ProjectStage` and `StageChangeLog` are unchanged.
   - A HoD direct correction succeeds and writes `StageChangeLog`.
   - `/health` is green.
3. **Pending proposals.**
   - They stay stored and untouched, as `Pending`, and are invisible: R-D2's Decision Centre does not know the type.
   - Tell HoDs that pending Actual corrections are frozen. A HoD may apply an urgent correction directly, recorded as usual in `StageChangeLog`.
   - No script mass-withdraws or edits revisions.
4. **Authoritative facts.** Only HoD/Admin direct writes, lifecycle writers and ARPP change them during recovery. Nothing is reconciled automatically.
5. **Returning to the corrected R-D3:**
   - Pending revisions reappear.
   - A revision whose stages changed during recovery fails its per-item baseline, shows "Needs revision: official facts changed", and can be rejected or revised.
   - An untouched revision remains decidable and is still revalidated at decision time.
6. **If R-D3 published wrong values** (the reason for recovery), correct them with HoD direct edits. The affected revisions' items and `StageChangeLog` rows give the exact before and after values. There is no automated reversal; the corrections are themselves logged.

**Proposed implementation requirement:** the PO cut-off in R-D2 (G, D-2) is what makes R-D3 → R-D2 governance-safe. Without it, the only governance-safe recovery is fix-forward, and the R-D2 floor would reopen direct PO publication. If the owner rejects the cut-off (K-14), the recovery plan must state that rolling back to R-D2 knowingly restores PO direct publication. That is a governance exception the owner must approve at the time.

### I.4 Compatibility

- Routes, form field names and JSON `ok` semantics are preserved. Fields are additive (`pendingApproval`, `stale`, `approval-required`, baseline fields).
- No configuration keys are added. No Internet dependency is introduced; all assets are local.
- Operator scripts are unchanged. The release notes add the R-D2 and R-D3 smoke steps and the recovery checklist in I.3.

---

## J. Acceptance criteria

Phase D is complete when all of the following hold:

1. **No PO write path to official facts.**
   - From R-D2, no code path lets a user holding only the `Project Officer` role change `ProjectStage.ActualStart`, `CompletedOn`, `RequiresBackfill` or the auto-completion flags.
   - Proven by tests for `EditActuals` and `BackfillApply`.
   - The F.5 matrix confirms that every other writer requires Admin/HoD, system or creation authority. Row 9 (`Procurement/Edit`) writes only the backfill flag, and only for stages whose dates exist; it is covered by hariomahlawat/ProjectManagement#2673, which is merged before R-D3.
2. **Approval publishes exactly what was reviewed.** HoD approval of revision N publishes exactly N's proposed values. `StageChangeLog` To-values, revision items and `ProjectStage` agree, asserted in tests.
3. **Stale decisions are rejected without mutation.** Approve or reject against a superseded, withdrawn, decided, epoch-mismatched or other-project revision fails with no mutation. Approve against a changed baseline fails with no mutation.
4. **Stale edits are rejected without mutation.**
   - S1–S6 (E.4) pass at service and page level.
   - No submission supersedes a revision it was not based on, or records items against an official stage state it did not render.
   - Posts without the baseline marker are refused for proposals (and, from R-D3, for direct saves).
5. **Required evidence is atomic with the change it records.**
   - For submit, supersede, withdraw, reject, approve and direct save, failure injection on any required-evidence write leaves no state change and no partial evidence (E.1).
   - An approval never commits Actual dates without its revision closure and `StageChangeLog` rows.
   - Post-commit `AuditLogs` or notification failure never reverses or misreports a committed action.
6. **Shared validation on every Actual correction and backfill boundary, as scoped in F.5.**
   - Rows 1–4 enforce the shared validator, and produce the same result for an identical final state from Direct, Backfill, Submission and Approval (a parameterized test across sources).
   - Rows 5–7 are warning-only and emit `Stages.ActualChronologyWarning`.
   - Row 8 is exempt by rule 17.
   - Phase D does **not** claim that lifecycle or ARPP writes are chronology-validated.
7. **The rule 8 and 25 examples hold.** FS completion 10→20 Jan with SOW start 11 Jan is rejected alone and accepted when SOW is also moved to 20 Jan. Convergence uses the latest dated branch. Skipped stages are transparent. Same-day is accepted.
8. **ARPP rule 17 holds.** Published-ARPP IPA completion cannot be changed by any Phase D path, including a race with publication (R5 on PostgreSQL). Draft ARPP does not lock.
9. **Lifecycle status is never changed by an Actual correction**, except the documented ARPP-effective IPA case.
10. **Concurrency and rollback are proven on PostgreSQL.** R1–R8, R12 and the approve failure-injection case pass in the PostgreSQL CI job.
11. **Recovery is rehearsed.** The I.3 R-D3 → R-D2 checklist has been executed once on a non-production copy and recorded in `docs/manual-tests`.
12. **Existing suites still pass:** B1/B2/C1/C2, lifecycle and ARPP. The CI checks are `npm test`, `npm run check:notebook-assets`, `dotnet build`, `dotnet test`, `lint:views` and the migration gate.
13. **Documentation is updated:** `docs/timeline.md`, `docs/timeline-proposal-roadmap.md` (G03, G04, G12, G13 closed; G11 status), `docs/data-domain.md` and `docs/phase-d-handoff.md`.
14. **Release notes are complete.** They contain the migration impact, outage requirement, smoke steps and the I.3 rollback/recovery classification for each release.

---

## K. Open decisions

Each decision is classified as either a **policy** decision for the business owner or an **implementation** choice, where the plan's default stands unless a reviewer objects. "Needed before" names the earliest step that depends on the answer. Only the bolded rows block work.

| # | Question | Governing rule / code today | Recommended default | Type | Needed before |
| --- | --- | --- | --- | --- | --- |
| **K-1** | May Admin approve or reject PO Actual corrections? | Rules 19/22 name HoD as the approver and grant "no new Admin approval rights". `ApprovalAuthorization.CanApproveProjectChanges` already lets Admin decide stage and plan requests, and Admin already has direct Actual authority (`EditActuals`) | Allow, via the existing helper. It grants no capability Admin lacks, since Admin can apply the same change directly | **Policy (authorization)** | **D-3d** |
| K-2 | Any HoD, or only `Project.HodUserId`? | Rule 19 says "HoD". Every path accepts any HoD; `StageDirectApplyService` documents this as deliberate | Any HoD, unchanged | Implementation (follows existing policy) | — |
| K-3 | May a HoD reject a revision whose baseline has changed? | Rule 24 targets decisions on superseded revisions; that epoch check still applies | Yes. Nothing is published; approval stays blocked | Implementation | — |
| K-4 | Should HoD direct edits or ARPP publication auto-close conflicting pending revisions? | Rule 17 requires superseding pending IPA *lifecycle* requests and gives ARPP value precedence. Direct apply supersedes lifecycle requests | No. Approval refuses (baseline and ARPP check), so precedence holds; "Needs revision" is shown. Automatic closure is a possible later refinement | Implementation (rule satisfied by refusal) | — |
| K-5 | May a PO propose clearing `CompletedOn` (returning a stage to backfill)? | Rule 9 needs an explicit authorised path; none exists today for anyone | Not in Phase D; HoD reopen remains the path | Policy, but the default preserves current product | — (a future request, not a blocker) |
| K-6 | After lead-PO reassignment, who may supersede or withdraw a pending revision? | Every PO right derives from the current `LeadPoUserId` | The current assignee only; the revision stays decidable | Implementation (follows the existing assignment model) | — |
| K-7 | May Actual start on a `Blocked` stage be corrected? | Rule 2 allows a retained start; `StageActualsUpdateService` refuses Blocked rows today | Keep refused | Implementation (preserves current behaviour) | — |
| K-8 | Notifications on submit and decision, and to whom? | Lifecycle requests send none | After commit: submit to `Project.HodUserId` if set; decision to the submitter | Implementation | — |
| K-9 | Is a PO justification note mandatory? | Lifecycle notes are required only for block/skip/reopen | Optional | Implementation | — |
| K-10 | Should date backfill clear `RequiresBackfill` when required *facts* are still missing? | `StageBackfillService` clears it unconditionally today | Preserve; track facts-backfill separately | Implementation (preserves current behaviour) | — |
| **K-11** | Accept per-branch convergence enforcement (F.3)? | Rule 6: the latest applicable predecessor completion controls. Today an undated branch disables all bounds | Accept. It blocks only violations that depend on changed values (F.4) | **Policy interpretation with production impact.** Direct HoD/Admin saves that pass today can be refused | **D-1a** |
| K-12 | Deployment topology, and a full outage per release? | Unverified | Required by the SOP | Operational | Release R-D2 (not development) |
| K-13 | Which governs migration application: the startup gate or the reviewed-SQL SOP (B.6 #1)? | The startup gate applies migrations | Operators confirm; fix the wrong doc | Operational / documentation | Release R-D2 (not development) |
| **K-14** | Accept the PO cut-off in R-D2, and deploy R-D2 and R-D3 in one window? | Rule 19: direct PO publication is a deviation. There is no existing switch (I) | Yes. It makes the R-D3 → R-D2 floor governance-safe (I.3) | **Policy (business continuity and authorization)** | **D-2** |

**Blockers:**

- **Before development:** K-11 blocks D-1a, K-14 blocks D-2 and K-1 blocks D-3d.
- **Before deployment:** K-12 and K-13 are needed before R-D2 is deployed.
- **Not blocking:** all other rows proceed on their stated defaults.

---

## Self-review

- **Complexity.**
  - The design adds two tables, three small services, one static token helper and no Draft state, and reuses the one existing gate.
  - There is no outbox, event bus, configuration switch or versioning framework.
  - Section C.1 rejects the generic-framework and `PlanVersion` options.
- **Authorization.**
  - Every new handler re-reads assignment on the server, inside the gate. Decide paths re-check role and self-approval; withdraw checks the current assignee.
  - From R-D2 no PO-only path publishes Actual dates, including in emergency recovery.
  - Read models expose proposals only to users who can view the project (inherited from Overview access).
  - The `Procurement/Edit` gap (B.6 #6) is fixed separately in hariomahlawat/ProjectManagement#2673 and is a prerequisite for R-D3.
- **Evidence.**
  - Required history is written in the same transaction as the change. Secondary `AuditLogs` and notifications run after commit.
  - Phase D does not adopt the in-transaction `IAuditService` pattern that carries a PostgreSQL abort risk (B.6 #7).
- **Concurrency.**
  - Every decision-time and submit-time read happens after the gate (and the row lock where publishing).
  - The editor baseline covers first submissions as well as replacements.
  - The one writer outside the gate that matters for IPA (the ARPP synchronizer) is covered by the row lock.
  - R1–R8 and R12 are mandated on PostgreSQL.
- **Validation scope.** Enforced on correction and backfill boundaries only; lifecycle writers are warning-only; ARPP is exempt by rule. This is stated in F.5 and J-6, not over-claimed.
- **Phase E creep.**
  - There is no cross-category identity, and lifecycle publication is untouched apart from observability. Plan services are not modified.
  - The only cross-category element is a read-only contradiction check at submission, which rule 18 already demands.
