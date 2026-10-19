# Phase D handoff — approved Actual correction and completion backfill

> **Status:** design/investigation handoff, not implementation authorization. Baseline: `master` after merged C2 PR #2670 (merge commit `4eb9ec5b7db91fcc099344fe2e3d18c18b24c184`). Source control merge is **not** evidence that C2 has been deployed. Verify the current branch, running deployment and applicable migration lineage before making release claims.

## How to read the repository

1. Read [AGENTS.md](../AGENTS.md), [engineering invariants](engineering-invariants.md), [current state](current-state.md), and [build/test guidance](build-and-test.md).
2. Read [canonical timeline business rules](timeline.md#canonical-timeline-business-rules) **in full**, including status/date matrix, explicit versus suggested chronology, skipped predecessors, ARPP-managed IPA, revision identity, audit, and final-state evaluation.
3. Read the [timeline roadmap](timeline-proposal-roadmap.md). Its gap table contains historical evidence: entries marked **open** still require investigation, and entries marked **closed** record completed fixes. Confirm each status against current code and tests rather than treating historical prose or phase labels alone as proof.
4. Inspect executable services, pages, EF model/migrations, service registration, and tests before proposing a design. Never treat this guide as proof that runtime behavior is already correct.

## Completed baseline — do not reimplement

B1 established actor-owned plan proposal inputs, publication authority, and legacy cutover. B2 established one canonical planned-timeline validator. C1 added exact reviewed revision/epoch decision guards and project-gated publication. **C2 merged in PR #2670:** PO plan editing while approval is pending, material supersession and exact withdrawal, immutable C2 submitted history and rejection correction Drafts, optimistic edit fingerprints, after-commit notifications and concurrency regressions. C2 introduced persisted terminal statuses without schema change. Do not restore a PO pending-edit lock, mutate submitted C2 PlanVersions, or treat legacy same-ID rejection behavior as the default.

This is **plan-only** architecture. It does not authorize direct PO Actual mutation or supply a cross-category Timeline Change Set. Preserve the independent planning publication rules.

## Phase D problem and target

The assigned Project Officer can currently submit Actual correction and mandatory completion backfill through paths that apply official dates directly. The Phase D business target is **PO proposes; HoD approves/rejects the exact Actual correction/backfill; official state changes only after an authorized, validated decision**. Backfill is a factual correction, not an independent PO publication privilege.

Do not silently infer lifecycle transitions from changed dates. HoD/Admin direct functionality stays separately permissioned where the current product permits it, with the same factual validity. Phase E (single cross-category Timeline Change Set spanning Plan, Actual and Lifecycle) and later UI consolidation are explicitly out of scope. Do not pretend a Phase D-only proposal is already the Phase E model.

## Verified starting points in merged source

| Area | Code to inspect | Current observed behavior / investigation question |
| --- | --- | --- |
| Actual edit entry | `Pages/Projects/Timeline/EditActuals.cshtml.cs` | `EditActualsModel.OnPostAsync` checks roles/project PO assignment and calls `StageActualsUpdateService.UpdateAsync` directly. Separate HoD/Admin authority exists; inspect exact resource policy and surrounding UI. |
| Actual mutation | `Services/Stages/StageActualsUpdateService.cs` | Validates inputs, loads project stages, uses `IProjectStageWorkflowPolicy`, detects pending stage decisions, resolves ARPP IPA authority and edits official actuals/history. Investigate validation scope, transaction, stale-read and final-state invariants. |
| Backfill entry | `Pages/Projects/Stages/BackfillApply.cshtml.cs` | Assigned PO or Admin/HoD may call `StageBackfillService.ApplyAsync` directly. Returns JSON, with validation and conflict responses. |
| Backfill mutation | `Services/Stages/StageBackfillService.cs` | Applies to Completed stages requiring backfill, checks IST limits, forbids managed IPA, clears flags and records `StageChangeLog`. Inspect graph-wide chronology and atomicity. |
| Stage workflow | `Services/Stages/`, `IProjectStageWorkflowPolicy` and related policy/validation services | Reuse canonical topology and existing status authority; inspect skipped nodes, convergence, reopening and pending decisions. |
| ARPP | `Services/Arpp/`, `IArppIpaStageAuthorityService` | Published ARPP governs IPA completion; draft/unsubmitted ARPP must not falsely complete or lock IPA. Preserve authority in proposed and decision-time projections. |
| Planning regression boundary | `Services/Plans/`, `Pages/Projects/Timeline/Review.cshtml.cs` | Keep exact plan decision identity, B2 projected planned validation and C2 nonblocking revision logic intact. Reuse proven patterns where appropriate; do not couple Phase D to PlanVersion without design evidence. |
| Persistence and audit | `Data/ApplicationDbContext.cs`, `Models/Stages/`, `Models/Plans/`, EF migrations | Inventory authoritative dates, stage logs, approvals, snapshots, version tokens and relational transaction/gate conventions. Determine whether D truly needs schema changes; never assume no migration is needed. |
| Tests | `ProjectManagement.Tests/EditActualsAuthorizationTests.cs` plus actuals/backfill/workflow/ARPP tests located through search | Build a scenario matrix from existing coverage; add real relational concurrency tests for cross-user decisions and rollback where warranted. |

## Non-negotiable target invariants

- PO draft, save, submit, revision, withdrawal and rejection must not mutate authoritative `ProjectStage` Actual dates or mandatory-backfill flags. PO-proposed changes become authoritative only through an authorized HoD approval decision. Existing independently authorized HoD/Admin direct-update paths remain permitted where current product policy allows, subject to the same validation, audit and transactional integrity requirements.
- Approval/rejection binds the exact reviewed submitted identity and submission epoch. Reject stale review after revision, withdrawal, prior decision, or relevant authoritative source change; avoid double-approval and stale publication.
- The assigned PO can continue private proposal work; no recreation of the removed C2 pending-edit lock. An ordinary PO must not gain HoD/Admin direct publication powers.
- Whole affected final-state chronology uses configured workflow dependencies, transparent skipped ancestry and latest required convergence boundary, including untouched descendants. Explicit same-day actual starts remain valid; next-day suggested starts are not mandatory facts.
- Enforce IST non-future actual dates, `ActualStart <= CompletedOn`, status/date matrix, optional Completed ActualStart, mandatory InProgress ActualStart, authorized completion-without-date backfill, explicit clearing and reopen rules. Do not fabricate observed dates.
- Published ARPP IPA completion remains authoritative; an ARPP draft is not publication. Avoid automatic changes to genuine ActualStart. Check direct-authorization exceptions against existing policy.
- Publish changes, decision evidence, history/audit and any dispatch records transactionally; notifications must not be delivered as successes before commit. Account for retry and concurrent connections.
- Preserve C2 persisted status compatibility, legacy plan proposal cutover and offline-LAN production deployment. No SaaS/CDN/runtime Internet dependency; release plans must address mixed-binary and rollback risks.

## Investigation and implementation planning deliverable

Before coding, produce a code-grounded design identifying:

1. Every Actual/backfill read and write surface (Razor, APIs, direct HoD/Admin, stage workflows, ARPP, background paths) and current exact permissions.
2. A minimal Phase D proposal/revision model with clear ownership, review status, identity, stale checks, historical evidence, and coexistence with existing plan and lifecycle records. Explain alternatives and why chosen.
3. A single projected final-state Actual validator, its dependency graph semantics, exact publication boundaries and source-change revalidation. Do not duplicate planned chronology rules.
4. Transaction/gate and relational isolation plan: concurrent HoD decisions, PO replacement/withdrawal, adjacent lifecycle mutations, and failed publication rollback.
5. Backfill semantics and ARPP/IPA exception matrix, including missing actual starts, explicit clears and status invariants.
6. Exact files/methods, migration impact and rollout/rollback approach for the offline production LAN. Treat production database/application configuration as **unverified**.
7. Tests and acceptance criteria, including SQLite/in-memory unit tests, real PostgreSQL concurrency/integration fixtures where applicable, and all relevant CI checks.
8. Explicit Phase E deferrals and proof that Phase D does not create incompatible parallel active cross-category change sets.

Document unresolved policy ambiguities as questions instead of guessing or silently granting permissions. **Do not implement until this plan has been reviewed and authorized.**

## Validation and handoff boundary

Run appropriate `npm ci`, `dotnet restore`, `npm test`, `npm run check:notebook-assets`, `dotnet build`, `dotnet test`, `npm run lint:views`, and PostgreSQL migration/integration CI, reporting environment limitations honestly. For documentation-only changes, verify links and code-referenced symbols rather than claiming all runtime suites are necessary.

Keep the planning PR Draft until review. Do not merge, deploy, alter existing migrations, or edit application code as part of the documentation handoff.
