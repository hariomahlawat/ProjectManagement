# PRISM ERP — Phase D Implementation Plan: Approved Actual Correction and Completion Backfill

> **Status:** Review draft. This is a plan, not authorization to implement.
>
> **Baseline:** `master` at `4e0e9769`, which includes #2667–#2671. The documentation baseline is `24bfdf70`.
>
> **Scope:** Investigation and design only. No production code, migration or PR is part of this document. Production deployment state, database version and configuration were **not** inspected and remain unverified.

---

## 0. Executive recommendation

**Build a small dedicated proposal model for Actual-date facts.** It should be project-scoped and immutable per revision. Keep it separate from `PlanVersion` and from `StageChangeRequest`. Deliver it in three releases.

- **Release D-1: shared Actual final-state validation and publication.** No schema change.
  - Extract one source-neutral `IActualTimelineValidationService` and one shared publication routine.
  - Enforce them inside the existing project transaction gate on today's direct paths: `StageActualsUpdateService` and `StageBackfillService`.
  - This closes gaps G12 and G13 for every caller, including HoD/Admin. Rollback is a binary swap.
- **Release D-2: additive schema, shipped dark.** Add two new tables, `ActualCorrectionRevisions` and `ActualCorrectionItems`, plus their EF mapping and migration. No behaviour changes.
  - The startup migrator rejects older binaries once a new migration is applied (`DatabaseStartupMigrator.EnsureNoUnknownAppliedMigrations`).
  - Isolating the schema step therefore lets the behavioural release (D-3) roll back with a binary swap to D-2, instead of a database restore.
- **Release D-3: PO proposals and HoD decision.**
  - An assigned PO's submissions through the existing `EditActuals` and `BackfillApply` routes become **proposals**.
  - HoD decides them in the existing Decision Centre (`Pages/Approvals/Pending`).
  - Approval re-validates the exact revision against current official facts inside the project gate, then publishes through the same routine HoD/Admin use directly.
  - HoD/Admin direct correction remains, with identical validation.

The key choices:

- **At most one actionable Actual-correction revision per project.** It is project-scoped, not stage-scoped.
- **No server-side Draft state.** Each PO save is a validated submission that supersedes the previous pending revision as a whole.
- **Exact-revision decisions** use compare-and-set on the submission epoch.
- **Stale-baseline detection is per stage.**
- **ARPP authority is enforced at submission and at decision.**

These choices satisfy rules 18–26 for the Actual category without building the Phase E cross-category Timeline Change Set. They also avoid parallel per-stage Actual lineages, which Phase E would otherwise have to unwind.

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
| 5 | `StageDecisionService.DecideAsync` runs `StageValidationService` before `LockProjectAsync` (lines ~295 and ~312). | A stale validation window against concurrent Actual publication. Fixed in D-1/D-3 (E.4). |
| 6 | `Pages/Projects/Procurement/Edit.cshtml.cs` has no project-assignment check, yet can clear `RequiresBackfill`. `Project` has no global `IsDeleted` query filter, so `EditActualsModel` does not exclude soft-deleted projects. | Resource-authorization defects outside Phase D's core. The second is fixed in D-1. The first is fixed separately in hariomahlawat/ProjectManagement#2673 (open, not yet merged). |

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

**Immutability.** After insert, only the status-closure fields change: `Status`, the `Decided*`/`Closed*` fields, `SupersededByRevisionId` and `DecisionNote`. Items are never updated. The service layer enforces this (there is no update path), and tests verify it.

**No Draft state.** The PO's working copy lives in the browser form. Every save is a validated submission. This deliberately differs from C2:

- An Actual correction is a few dates on a few rows. It has none of the planning policy that justified C2's private Drafts.
- Without Drafts there is no "material SaveDraft makes the old revision non-actionable" case, no private-draft visibility rules and no discard semantics.
- Rule 22 is met: the PO can create, revise, withdraw, resubmit and replace at any time; pending never blocks them.

The editor may keep an unsent form in `localStorage` as a convenience. It is not authoritative.

### C.4 Submission and resubmission (`ActualCorrectionProposalService.SubmitAsync`)

The input is the project, the requested changes (stage code, `ActualStart`, `ClearActualStart`, `CompletedOn`, optional per-item note), the revision the form was based on (`BasedOnRevisionId` and `BasedOnSubmittedOn`, or none), an optional PO note, and the entry point (`Editor` or `Backfill`).

1. Authorize: the PO must be the assigned lead and not Admin/HoD. The project must exist, not be deleted, be lifecycle `Active` and not legacy, matching what the UI already shows.
2. Open a `RelationalTransactionScope` and call `LockProjectAsync`.
3. Load the actionable `Pending` revision, if any.
   - If the form's based-on revision does not match it, return **Stale**: "Your pending update changed in another window or was decided. Reload to review the latest values."
   - This mirrors the C2 stale-tab rule. Legacy clients without the token are not expected; this is a new contract.
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
9. Otherwise, in the gate:
   - Conditionally close the previous pending revision with `UPDATE … SET Status = 'Superseded' WHERE Id = @prev AND Status = 'Pending'`, and set `SupersededByRevisionId`.
   - Insert the new revision and its items, with `Before*` values snapshotted from the locked official rows.
   - Commit.
   - After commit, register an audit entry (`Projects.ActualCorrectionSubmitted`) and the optional HoD notification.

Official `ProjectStage` rows are never written on this path, which is the core invariant.

### C.5 Withdrawal (`WithdrawAsync`)

- Takes the exact `RevisionId` and `SubmittedOn`.
- The caller must be the current assigned PO (K-6).
- Inside the gate, a compare-and-set moves `Pending` to `Withdrawn`. A stale or already-decided revision returns `NotActionable`.
- No official mutation.

### C.6 HoD approval and rejection

- **`ApproveAsync(revisionId, expectedSubmittedOn, approver)`**
  1. Authorize with `ApprovalAuthorization.CanApproveProjectChanges` (see K-1). Reject self-approval (`SubmittedByUserId == approver`).
  2. Open the transaction, take the gate, then `SELECT … FOR UPDATE` the project's `ProjectStages` rows (E.3).
  3. Re-read the revision. It must be `Pending` with matching `SubmittedOn` and project; otherwise return **Stale** ("A newer revision is available. Review the latest update before deciding.").
  4. **Baseline check:** for each item, the locked official `Status`, `ActualStart`, `CompletedOn` and `RequiresBackfill` must equal the item's `Before*` values. Any difference returns **SourceChanged**, and nothing is mutated.
  5. Resolve ARPP authority *after* the row lock. Validate the final state (official plus items) with source `Approval`.
  6. Compare-and-set claim: `UPDATE … SET Status = 'Approved', DecidedByUserId, DecidedOn WHERE Id AND Status = 'Pending' AND SubmittedOn = expected`. It must affect exactly one row.
  7. Publish through `ActualFactPublisher.Apply` (C.7), which writes the stage fields and one `StageChangeLog` per stage. The action is `Backfill` or `ActualsUpdated`, `UserId` is the approver, and the note reads "Approved actual correction rev {RevisionNo} (#{Id}) submitted by {submitter} at {time}".
  8. Commit. After commit, write the audit entry and the PO notification.
  9. Any exception rolls back the claim and the publication; the revision stays `Pending`.
- **`RejectAsync(revisionId, expectedSubmittedOn, note)`**
  - Same identity check (`Pending` with matching epoch) and compare-and-set to `Rejected`. A note is required; the Decision Centre already requires remarks.
  - No baseline or validity check is needed, because nothing publishes. A HoD can therefore clear a revision whose basis has changed (K-3).
  - No official mutation.
- Approval never changes `Status`. It does not run realignment, and neither direct correction nor backfill does today. Kept identical.

### C.7 Direct HoD/Admin authority

These paths are unchanged in *who* may act:

- `EditActuals` for Admin and HoD.
- `BackfillApply` for Admin and HoD.
- `StageDirectApplyService`, `StageDecisionService`, `HistoricalStageRecordService` and the ARPP synchronizer.

The direct Actual paths (1 and 2) are rebuilt on the shared components:

- **`ActualChangeNormalizer`**: input semantics.
- **`IActualTimelineValidationService`**: validity, with source `Direct`.
- **`ActualFactPublisher`**: field effects, logs and audit.

They also gain the gate, a transaction, the row lock and **per-stage stale-save tokens**.

- The form carries the original `ActualStart`, `CompletedOn` and `Status` per row as hidden fields.
- If they differ from the locked row, the save returns "This stage changed since you opened it. Reload and review."
- When tokens are absent (older cached pages), the current behaviour is kept, to preserve route compatibility.

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
- The startup gate refuses older binaries after the migration. Rolling back D-2 therefore requires the database backup or a reviewed reverse script. Rolling back D-3 needs only a binary swap to D-2.

---

## E. Transaction and concurrency design

### E.1 Boundary

Every Phase D authoritative or proposal-state write runs as one `RelationalTransactionScope` with this sequence:

1. `LockProjectAsync(projectId)`
2. Row-lock the project's stages (approval and direct saves only)
3. Read
4. Validate
5. Compare-and-set
6. Write
7. Commit
8. `RegisterAfterCommit` audit and notifications

Validation always happens **after** the gate and the row lock, never before.

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

### E.4 Stale-decision guards

1. Revision `Status = 'Pending'` with exact `SubmittedOn`, checked inside the gate and enforced again by the compare-and-set claim.
2. Per-item baseline equality against locked official rows.
3. Final-state revalidation against current official facts, the current IST date, current ARPP authority and the current workflow and PNC.
4. Self-approval refusal.

`StageDecisionService.DecideAsync` should also run its existing `StageValidationService` call **after** `LockProjectAsync`. This is a small move, scheduled in D-1, with a regression test for a concurrent Actual publication.

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
| R8 | Two PO tabs submit concurrently | One wins; the other is Stale (based-on mismatch); the filtered unique index is the backstop | PG |
| R9 | Publication fails mid-way (injected `SaveChanges` failure or CHECK violation) | Full rollback; revision still `Pending`; no notification | SQLite |
| R10 | Double-click approve | Idempotent AlreadyDecided | SQLite |
| R11 | Old and new binary overlap | Not supported. Prevented by the outage SOP (section I) | Operational |

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

### F.5 Enforcement points

| Boundary | Source | Inside gate |
| --- | --- | --- |
| `StageActualsUpdateService.UpdateAsync` (HoD/Admin direct) | Direct | yes |
| `StageBackfillService.ApplyAsync` (HoD/Admin direct) | Backfill | yes |
| `ActualCorrectionProposalService.SubmitAsync` | ProposalSubmission | yes |
| `ActualCorrectionProposalService.ApproveAsync` | Approval | yes, after the row lock |
| Lifecycle paths (`StageDirectApplyService`, `StageDecisionService`, `HistoricalStageRecordService`) | Observability only in Phase D (step D-1d) | — |

For the lifecycle paths, a warning is logged when a lifecycle publication would create a new final-state chronology violation. Enforcement there is a lifecycle behaviour change and is deferred to Phase E, or to a separately approved step.

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
| `Services/Stages/StageActualsUpdateService.cs` | Transaction, gate, `FOR UPDATE` load, normalizer, validator (`Direct`), publisher. Keep the pending-request conflict for direct saves. Audit via `RegisterAfterCommit`. Optional per-row stale tokens |
| `Services/Stages/StageBackfillService.cs` | Same, with source `Backfill`. Keep 409 conflict semantics. Add audit `Projects.StageBackfilled` |
| `ViewModels/ActualsEditorVm.cs`, `Pages/Projects/Timeline/_EditActualsForm.cshtml`, `Pages/Projects/_BackfillForm.cshtml`, `wwwroot/js/projects/overview.js` | Optional original-value tokens (additive fields) |
| `Pages/Projects/Timeline/EditActuals.cshtml.cs` | Exclude `IsDeleted` projects. Map a new stale result to a message |

Tests:

- Update `StageActualsUpdateServiceTests` and `StageBackfillServiceTests`: graph errors now raised for backfill; untouched successor.
- Rollback on failure; stale token rejection; legacy no-token behaviour unchanged.
- Existing `EditActualsAuthorizationTests` must still pass unchanged in D-1, because authority is not altered yet.
- ARPP tests in `ProjectManagement.Tests/Arpp/ArppIpaStageSynchronizerTests.cs` (`ActualsEdit_*`, `CompletionBackfill_*`) unchanged.

### D-1c — Decision TOCTOU fix

| Files | Change |
| --- | --- |
| `Services/Stages/StageDecisionService.cs` | Move `LockProjectAsync` and the transaction above the validation block. Behaviour is otherwise identical |

Tests: SQLite ordering test, plus a PG scenario in a new `StageActualsConcurrencyPostgresFixture` (see H).

### D-1d — Lifecycle observability (optional, recommended)

| Files | Change |
| --- | --- |
| `StageDirectApplyService.ApplyCoreAsync`, `StageDecisionService.DecideAsync` | After mutation and before commit, run the validator in warn mode. Log structured warning `Stages.ActualChronologyWarning`. Never block |

Tests: the warning is emitted; the outcome is unchanged.

### D-1e — Documentation

Update `docs/timeline.md` ("Actual-date editing and backfill"), the `docs/timeline-proposal-roadmap.md` G12/G13 status (closed for direct paths), `docs/data-domain.md` (stale backfill and lock text), and this plan's status.

### D-2 — Schema, shipped dark

| Files | Change |
| --- | --- |
| **New** `Models/Stages/ActualCorrectionRevision.cs`, `Models/Stages/ActualCorrectionItem.cs` | Entities (D.2) |
| `Data/ApplicationDbContext.cs` | `DbSet`s and configuration, following the `StageChangeRequest` provider-conditional filter/check pattern |
| **New** migration via `tools/Add-PrismMigration.ps1`; `Migrations/immutable-migration-ids.txt` | Additive |
| `Services/Projects/ProjectModerationService.cs` (purge) | Delete revisions and items with the project |
| `Infrastructure/ApplicationDatabaseSchemaValidator.cs` | Validate the new tables, if this validator enumerates critical tables |

Tests: `ApplicationDatabaseMigrationsTests`, the `PostgresMigrationIntegrationTests` physical-schema assertions, and a purge test.

### D-3a — Proposal service

| Files | Change |
| --- | --- |
| **New** `Services/Stages/ActualCorrectionProposalService.cs` | `SubmitAsync`, `WithdrawAsync`, `ApproveAsync`, `RejectAsync`, `GetActionableAsync` (C.4–C.6). Result types are a discriminated record (`Success`, `Unchanged`, `Stale`, `SourceChanged`, `Validation`, `Conflict`, `Forbidden`, `NotFound`) |
| **New** `Services/Stages/ProjectedLifecycleState.cs` | Extracted from `StageRequestService` private types. `StageRequestService` is refactored to use it with no behaviour change, proven by its existing 18 tests |
| `Services/Stages/IStageNotificationService.cs` or a new `IActualCorrectionNotificationService` | After-commit notifications (K-8). Optional |

Tests (SQLite, plus PG in `ActualCorrectionConcurrencyPostgresFixture`, wired into `PostgresMigrationIntegrationTests.CompleteStartupBoundary_MigratesAndValidatesBothContexts`, R1–R10):

- Submit writes no `ProjectStage` values.
- Supersede, withdraw, unchanged, empty, backfill carry-forward, kind derivation.
- Approval publishes the exact values; baseline change gives `SourceChanged`; ARPP published mid-flight.
- Self-approval; reject without validity check; immutability of items.

### D-3b — PO routing on existing routes

| Files | Change |
| --- | --- |
| `Pages/Projects/Timeline/EditActuals.cshtml.cs` | If the user is the PO-only assigned lead, call `SubmitAsync` and set a flash message ("Sent to HoD for approval. You can revise it at any time until it is approved."). Admin/HoD keep the direct path. Same route and form names |
| `Pages/Projects/Stages/BackfillApply.cshtml.cs` | Same routing. JSON gains `pendingApproval: true, revisionNo` (additive; `ok` stays `true`) |
| **New** handler `EditActuals?handler=Withdraw` | Exact id plus epoch |
| `wwwroot/js/projects/overview.js` (backfill submit), `wwwroot/js/projects/actuals-edit.js` | Handle `pendingApproval`. Optionally consume server-emitted predecessor lists for graph-correct client hints (G11); the server stays authoritative |

Tests:

- Rewrite the PO cases in `EditActualsAuthorizationTests` (`OnPostAsync_PreservesCurrentAuthority`, `OnPostAsync_IpaCompletionCorrectionUsesConditionalPublishedAuthority`, `OnPostAsync_IpaStartRemainsSeparatelyCorrectableUnderNormalRoleAuthority`). The assigned PO now creates a revision and official values stay unchanged; HoD/Admin are unchanged.
- Add `StageBackfillAuthorizationTests` cases: assigned PO gives a proposal; HoD gives direct.
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
| Authorization | Assigned PO gets a proposal; unassigned PO is forbidden; Viewer is forbidden; HoD/Admin go direct; self-approval refused; Admin decide per K-1; soft-deleted project gives NotFound | Page plus service |
| Status/date matrix | InProgress start required; no completion on InProgress; Completed start clearable; completion not clearable; future dates (IST 23:59/00:01); start > completion | Unit |
| Graph | V1 vs V2 ordering; skipped ancestry; convergence (dated plus undated branch); PNC applicable on/off; same-day; next-day not enforced | Unit |
| Final state | Predecessor change breaks an untouched successor (error); coordinated fix (pass); pre-existing violation (warning) | Unit plus service |
| Backfill | Kind derivation; carry-forward with pending corrections; 409 when no longer needed; auto-completed stage; facts-only flag untouched | Service |
| ARPP | None, draft or published × IPA start or completion; publication between submit and approve; startup sync | Service plus PG (R5) |
| Lifecycle interplay | Pending reopen, skip or completion contradiction at submit; lifecycle approval first then actual approval (SourceChanged); actual approval first then lifecycle decision revalidates | Service |
| Rejection / withdrawal / supersession | Stale epoch; superseded N not decidable; withdraw by a non-owner refused | Service |
| Rollback | Injected failure after claim; CHECK constraint violation; no notification | SQLite |
| Concurrency | R1–R8 with two `ApplicationDbContext` connections and a held lock, following `PlanRevisionConcurrencyPostgresFixture` | **PostgreSQL** |
| Regression (unchanged) | All B1/B2/C1/C2 suites; `StageRequestServiceTests`; `StageDirectApplyServiceTests`; ARPP suites | Existing |
| Front-end | `npm test` (actuals JS), `npm run lint:views` | CI |

Green SQLite tests do not prove relational locking. R1–R8 must run in the `.github/workflows/database-migrations.yml` PostgreSQL job before D-3 is released.

---

## I. Deployment and compatibility

Production state is **unverified**. Before any release, operators must establish:

- the deployed binary and migration head, using `ops/database/production-migration-inventory.sql`;
- the number of IIS sites, app pools and worker processes that can write.

### I.1 Release plan

| Release | Contents | DB change | Rollback |
| --- | --- | --- | --- |
| R-D1 | D-1a … D-1e | none | Binary swap to the previous release (the SOP's `releases\previous`) |
| R-D2 | D-2 | additive migration | Requires a database restore or a reviewed reverse script; older binaries will not start (`EnsureNoUnknownAppliedMigrations`). Low risk: no behaviour |
| R-D3 | D-3a … D-3e, released together | none | Binary swap to R-D2. Pending revisions remain stored and inert. While on R-D2, POs regain direct edit (the pre-D behaviour); on re-upgrade, stale revisions fail the baseline check safely |

### I.2 Procedure for each release (offline SOP §D)

1. Back up the database.
2. Place `app_offline.htm` and stop **every** process that can write: IIS app pools, plus any other instance. Mixed old/new writers are unsupported. An old binary would let a PO publish directly while the new one expects proposals.
3. R-D2 only: the startup gate applies the migration under its advisory lock. Generate `migration.sql` (`dotnet ef migrations script -i`) beforehand for review and the release note. Resolve discrepancy B.6 #1 with the operators beforehand.
4. Deploy the complete publish folder; run `--compendium-offline-self-test`; check `/health`.
5. Smoke test:
   - R-D1: HoD direct correction, plus a chronology rejection message.
   - R-D3: PO submits; HoD sees it in the Decision Centre; approve publishes; reject leaves official values unchanged.
6. Tell users to reload open Overview pages. Pre-cutover forms posted after R-D3 route to proposals for POs, which is safe.

### I.3 Compatibility

- Routes, form field names and JSON `ok` semantics are preserved. Fields are additive (`pendingApproval`, tokens).
- No configuration keys are added. No Internet dependency is introduced; all assets are local.
- Operator scripts are unchanged.

---

## J. Acceptance criteria

Phase D is complete when all of the following hold:

1. **No PO write path to official facts.** No code path lets a user holding only the `Project Officer` role change `ProjectStage.ActualStart`, `CompletedOn`, `RequiresBackfill` or the auto-completion flags. Proven by tests for `EditActuals` and `BackfillApply`, and by a search showing every writer listed in B.1 with its gate.
2. **Approval publishes exactly what was reviewed.** HoD approval of revision N publishes exactly N's proposed values. `StageChangeLog` To-values, revision items and `ProjectStage` agree, asserted in tests.
3. **Stale decisions are rejected without mutation.** Approve or reject against a superseded, withdrawn, decided, epoch-mismatched or other-project revision fails with no mutation. Approve against a changed baseline fails with no mutation.
4. **One validator, every Actual boundary.** The same validator result is produced for an identical final state from Direct, Backfill, Submission and Approval. This is a parameterized test across sources.
5. **The rule 8 and 25 examples hold.** FS completion 10→20 Jan with SOW start 11 Jan is rejected alone and accepted when SOW is also moved to 20 Jan. Convergence uses the latest dated branch. Skipped stages are transparent. Same-day is accepted.
6. **ARPP rule 17 holds.** Published-ARPP IPA completion cannot be changed by any Phase D path, including a race with publication (R5 on PostgreSQL). Draft ARPP does not lock.
7. **Lifecycle status is never changed by an Actual correction**, except the documented ARPP-effective IPA case.
8. **Concurrency is proven on PostgreSQL.** R1–R8 pass in the PostgreSQL CI job.
9. **Existing suites still pass:** B1/B2/C1/C2, lifecycle and ARPP. The CI checks are `npm test`, `npm run check:notebook-assets`, `dotnet build`, `dotnet test`, `lint:views` and the migration gate.
10. **Documentation is updated:** `docs/timeline.md`, `docs/timeline-proposal-roadmap.md` (G03, G04, G12, G13 closed; G11 status), `docs/data-domain.md` and `docs/phase-d-handoff.md`.
11. **Release notes are complete.** They contain the migration impact, outage requirement and rollback path for each release.

---

## K. Open decisions (require business or owner confirmation)

| # | Question | Code today | Recommendation |
| --- | --- | --- | --- |
| K-1 | May Admin approve or reject PO Actual corrections? | Admin may decide stage and plan requests (`ApprovalAuthorization`) | Reuse the existing helper for consistency, unless the business says HoD only |
| K-2 | Any HoD, or only `Project.HodUserId`? | Any HoD everywhere | Any HoD, consistent with current policy |
| K-3 | May a HoD reject a revision whose baseline has changed? | n/a | Yes (no mutation); approval blocked |
| K-4 | Should HoD direct edits or ARPP publication auto-close conflicting pending revisions? | Direct apply and ARPP auto-supersede *lifecycle* requests | No in Phase D. Leave them non-approvable with a "Needs revision" indicator; revisit in E |
| K-5 | May a PO propose clearing `CompletedOn` (returning a stage to backfill)? | No path allows it (blank preserves) | Out of scope; HoD reopen remains the path |
| K-6 | After lead-PO reassignment, may the new PO supersede or withdraw the previous PO's pending revision? | n/a | Yes, the current assignee only |
| K-7 | May Actual start on a `Blocked` stage be corrected? | Rejected (only InProgress/Completed are editable) | Keep rejected |
| K-8 | Notifications on submit and decision, and to whom? | None for lifecycle requests | Submit goes to the project HoD if set, otherwise the Decision Centre only; decision goes to the submitter; after commit |
| K-9 | Is a PO justification note mandatory? | Lifecycle requires notes only for block/skip/reopen | Optional, for parity with the editor today |
| K-10 | Should date backfill clear `RequiresBackfill` when required *facts* are still missing? | Yes (`StageBackfillService` sets false unconditionally) | Preserve; track facts-backfill separately |
| K-11 | Accept the per-branch convergence tightening (F.3)? | Undated branch suppresses all bounds | Accept. Blocking applies only to changed values (F.4) |
| K-12 | Deployment topology: single IIS instance, and is a full outage acceptable per release? | Unverified | Required for R-D2 and R-D3 |
| K-13 | Which governs migration application: the startup gate or the reviewed-SQL SOP (B.6 #1)? | Startup gate applies | Operators to confirm; update whichever doc is wrong |

---

## Self-review

- **Complexity.** There are two tables, three small services and no Draft state, and one existing gate is reused. Section C.1 rejects the generic-framework and `PlanVersion` options.
- **Authorization.** Every new handler re-checks assignment on the server. Decide paths re-check role and self-approval. Withdraw checks the current assignee. Read models expose proposals only to users who can view the project (inherited from Overview access). The `Procurement/Edit` assignment gap (B.6 #6) is outside Phase D and is fixed separately in hariomahlawat/ProjectManagement#2673.
- **Concurrency.** Every decision-time read happens after the gate and the row lock. The one writer outside the gate that matters for IPA (the ARPP synchronizer) is covered by the row lock, and R1–R8 are mandated on PostgreSQL.
- **Phase E creep.** There is no cross-category identity, lifecycle publication is untouched apart from observability, and plan services are not modified. The only cross-category element is a read-only contradiction check at submission, which rule 18 already demands.
