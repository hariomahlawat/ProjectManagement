# Phase D — Actual correction workflow: acceptance and release readiness (D-3e)

> **Classification: A — Acceptance passed; release prerequisites remain.** The D-2 + D-3 Actual correction workflow is functionally accepted on the evidence below. It is **not** release-ready. Release blocker #2683 and operator decisions K-12 and K-13 are open, and the coordinated release has not been authorized.
>
> Production deployment state, database version and configuration were **not** inspected. Nothing here claims they were.

## 1. Status ladder

These are separate states. Only the first two are met.

| State | Status |
| --- | --- |
| Code merged | **Yes.** D-1a–D-1e, #2680, D-2, D-3a/b, D-3c and D-3d, all on `master` (§2) |
| Acceptance passed | **Yes, with this PR:** the end-to-end, service, PostgreSQL and UI evidence below, plus three corrected defects (§6) |
| Release blockers closed | **No.** #2683 blocks packaging. K-12 and K-13 need operator answers (§8) |
| Deployment authorized | **No** |
| Production deployment complete | **No.** R-D2 and R-D3 have not been released, and K-14 requires them in one window |

## 2. Scope and merged milestones

| Milestone | PR | Merge |
| --- | --- | --- |
| D-1a shared validator | #2675 | `b5ef8030` |
| D-1b direct correction/backfill safety | #2676 | `07313590` |
| D-1c lifecycle and ARPP concurrency | #2677 | `307542b2` |
| D-1d chronology observability | #2678 | `119d6cf3` |
| D-1e documentation | #2679 | `8480a880` |
| HoD direct apply vs ARPP verify | #2680 | `207acd82` |
| D-2 proposal schema, PO cut-off | #2681 | `e3e8ea85` |
| D-3a/b submission and revisions | #2685 | `736458c3` |
| D-3c editor integration | #2686 | `0720b037` |
| D-3d decisions | #2687 | `0277a1bb` (D-3e baseline) |

## 3. How acceptance was established

The test pyramid used:

1. **Service level.**
   - `ActualCorrectionProposalServiceTests`: submission, revisions, withdrawal.
   - `ActualCorrectionDecisionTests`: approval, rejection, Decision Centre read models.
2. **Page model and authorization.**
   - `ActualCorrectionProposalPageTests` (D-3c handlers).
   - `EditActualsAuthorizationTests`, `DirectActualsPublicationTests`.
3. **End to end over HTTP (new in D-3e).** `Acceptance/ActualCorrectionEndToEndTests` runs the **real PRISM host** through `WebApplicationFactory`, including:
   - routing, authentication, authorization policies and antiforgery;
   - Razor rendering and DI (proving the optional proposal services are actually injected into `EditActuals`, `BackfillApply`, `Overview` and `ApprovalDecisionService`);
   - the Decision Centre pages.

   Details of the setup:
   - The host runs on a shared SQLite database, so transactions and compare-and-set statements are real. Startup uses a non-relational context, because the startup migration boundary validates the PostgreSQL catalogue (§7).
   - Forms are posted exactly as rendered, **without JavaScript**, so the server-rendered form alone must carry the user's intent.
4. **Rendered form and JavaScript contract.** The HTTP tests post exactly the controls a browser without scripts submits, but they do not execute `actuals-edit.js` or the backfill script inside a browser.

   The rendered form and JavaScript contract are covered by: `actuals-edit-*.test.js` (jsdom) and the markup contract tests.
5. **Real PostgreSQL:** the migration gate's `PostgresMigrationIntegrationTests`. It is one test that runs every fixture in sequence, including:
   - `ActualCorrectionSchemaPostgresFixture`;
   - `ActualCorrectionProposalConcurrencyPostgresFixture`;
   - `ActualCorrectionDecisionConcurrencyPostgresFixture`.

**Browser testing was not performed.** The repository has no browser-automation infrastructure; no browser rendered or exercised the workflow. The HTTP tests cover the server contract without JavaScript, and jsdom covers the scripts. A manual browser checklist is in §10.

## 4. End-to-end acceptance matrix

"E2E" is `ActualCorrectionEndToEndTests` (HTTP, real host). "Svc" is the service tests, "Page" the page-model tests, "PG" the PostgreSQL fixtures and "JS" the jsdom tests.

### A. PO submit

| Requirement | Result | Evidence |
| --- | --- | --- |
| Open the existing editor; official dates shown; **Submit for approval** | Satisfied | E2E `PoSubmitsThroughTheEditor…`, Page |
| Correct one or several dates; optional justification | Satisfied | E2E (IPA + note); Svc multi-row |
| Confirmation says nothing was published | Satisfied | E2E flash "Official dates are unchanged" |
| Reopen: pending status, proposed values prefilled, official shown | Satisfied | E2E `Revision 1 is awaiting…`, prefilled `2026-01-11`, "Official: start 12 Jan 2026" |
| No mutation of `ProjectStage`, `StageChangeLog`, lifecycle or plan | Satisfied | E2E and Svc (official values and log counts); submission writes only proposal tables (`ActualCorrectionProposalService.SubmitAsync`) |

### B. PO revise and withdraw

| Requirement | Result | Evidence |
| --- | --- | --- |
| Identical resubmission creates no revision | Satisfied | Svc `EquivalentResubmission…`, Page `…PostedUnchanged…` |
| Changed content creates an immutable successor; superseded evidence intact | Satisfied | Svc `SuccessorRevision…`, PG successor race |
| Editor resubmission keeps every pending item (incl. backfill) | Satisfied | E2E `ReopenedEditorWithoutJavaScript…`, Page |
| Backfill resubmission carries unrelated items | Satisfied | Svc, Page, E2E |
| Explicit start clear survives reopening | **Satisfied after fix D3E-1** | E2E (failed before the fix) |
| A date typed over a pending clear without JavaScript is neither discarded nor silently cleared | **Verified correct** (§6a) | E2E `DateTypedOverAPendingClearWithoutJavaScript…`; JS case C |
| Revision and item notes keep their meaning | Satisfied | Svc hash/note tests; E2E justification shown to reviewer |
| Withdraw closes only the exact revision; publishes nothing; stale refused | Satisfied | Svc `Withdraw…`, Page `Withdraw…`, PG approve-vs-withdraw |
| Reassignment keeps evidence (K-6) | Satisfied | Svc `NewLeadPo…`, `ReassignedFormerPo…`; D-3d self-decision after reassignment |

### C. HoD/Admin approve

| Requirement | Result | Evidence |
| --- | --- | --- |
| Listed in the Decision Centre; recorded vs proposed; submitter and justification | Satisfied | E2E queue and detail; Svc `DecisionCentre_Lists…` |
| Approve exact revision; facts = validated final values | Satisfied | E2E, Svc, PG |
| Mandatory history correct; decision metadata persisted | Satisfied | E2E (log action, from/to, reviewer; revision status/closer/remarks), Svc |
| Leaves the queue; Overview shows official values; PO no longer sees pending | Satisfied | E2E |

### D. HoD/Admin reject

| Requirement | Result | Evidence |
| --- | --- | --- |
| Closes exact revision; evidence immutable; no facts or history | Satisfied | E2E `AdminRejects…`, Svc |
| Reviewer and remarks retained; cannot be approved later | Satisfied | E2E (stale HoD tab refused) |
| PO can understand the outcome | **Satisfied after fix D3E-2** | E2E (failed before the fix) |

### E. Self-decision and role changes

| Requirement | Result | Evidence |
| --- | --- | --- |
| PO-only cannot decide or reach the Decision Centre | Satisfied | E2E, Svc |
| Unassigned PO cannot submit, even with a forged form | Satisfied | E2E `CrossedForms…`, Page |
| Viewer read-only | Satisfied | Page, Svc |
| HoD/Admin and mixed roles publish directly | Satisfied | E2E (HoD direct save), Page, `DirectActualsPublicationTests` |
| Submitter promoted to HoD cannot decide (buttons disabled; forced post 403) | Satisfied | E2E `SubmitterPromotedToHod…`, Svc |
| Proposal form never becomes direct publication after a role change, and the reverse | Satisfied | E2E (same browser session, roles changed) |
| Forged epoch or revision id fails safely | Satisfied | E2E, Svc |

### F. Chronology and lifecycle

| Requirement | Result | Evidence |
| --- | --- | --- |
| Predecessor change after submission makes approval invalid; nothing published | Satisfied | Svc `ProposalMadeInvalid…` |
| Interdependent multi-stage proposal | Satisfied | Svc multi-stage approval; D-1 validator suite |
| Compatible pending lifecycle request does not block; contradictory one does | Satisfied | Svc `PendingLifecycleRequests…`, D-3a/b F.6 tests |
| Lifecycle decided before approval | Satisfied | Svc (request decided, then approval succeeds) |
| Lifecycle update races approval | Satisfied | PG `ContradictoryLifecycleRequestCommittedFirst…` |
| Backfill and correction coexist | Satisfied | E2E `ApprovalOfAMixedCorrectionAndBackfill…` |
| Stage applicability changes (status change after submission) | Satisfied | SourceChanged via `BeforeStatus`; validator `StatusNotEditable` (D-1) |

### G. ARPP and IPA authority

| Requirement | Result | Evidence |
| --- | --- | --- |
| Published IPA completion cannot be proposed; start correction allowed | Satisfied | Svc `PublishedArppIpa…` |
| Republished ARPP date before approval refused | Satisfied | Svc `RepublishedArppCompletion…` |
| ARPP sync racing approval: approval waits on row lock and refuses; ARPP value stands | Satisfied | PG `ArppWriteOutsideTheGate…` |
| Direct publication consistent with ARPP | Satisfied | D-1 suites, #2680 |
| Missing-IPA-row insert race | **Deferred** (accepted limitation from #2680) | Phase D plan, D-1 verification findings |

### H. Failure recovery

| Requirement | Result | Evidence |
| --- | --- | --- |
| Stale stage token; stale pending baseline; changed Before* | Satisfied | Svc, Page, E2E |
| Conflicting submission from another browser | Satisfied | Svc S1–S2, PG first-submission race |
| Concurrent approve/approve, approve/reject, approve/withdraw, approve/successor | Satisfied | PG |
| Failure during history insert, closure, publication | Satisfied | SQLite failure injection (decision, submit); PG history and claim failure |
| Audit failure after commit | Satisfied | Svc `PostCommitAuditFailure…` |
| Cancellation before commit | Partially satisfied | Not separately tested. Every path runs inside one `RelationalTransactionScope`, so a cancellation before `CommitAsync` rolls back like any other exception. This is covered by the failure-injection tests |

### Decision Centre

| Requirement | Result | Evidence |
| --- | --- | --- |
| In queue and count; filter by type | Satisfied | E2E, Svc |
| Access control protects details (PO-only gets nothing) | Satisfied | E2E, Svc |
| Own submission not decidable; SourceChanged offers no approval but allows rejection | Satisfied | E2E |
| Decided, withdrawn and superseded not actionable | Satisfied | E2E, Svc |
| Accurate confirmations; failures never claim publication | Satisfied | E2E, Svc `ToApprovalResult` |
| Approval confirmation names the published Actual dates | **Improved in D-3e** (observation 2) | `DetailsModel.GetApproveConfirmation` |

## 5. Authorization matrix (verified)

| Actor | Submit | Direct publish | Approve/reject |
| --- | --- | --- | --- |
| Assigned PO-only | Yes | No (refused, "access changed") | No |
| Unassigned PO-only | No (403) | No | No |
| HoD / Admin | Not routed | Yes | Yes |
| HoD+PO / Admin+PO (assigned) | Not routed | Yes | Yes, except their own revisions |
| Submitter later promoted | n/a | Yes | **No** (`SelfDecisionForbidden`) |
| Viewer | No | No | No |

## 6. Confirmed defects and resolutions

Each defect was reproduced by an HTTP acceptance test that failed before the fix.

| ID | User action | Expected | Observed | Root cause | Severity | Blocks acceptance? | Fix |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D3E-1 | PO with a pending "clear start" reopens the editor and submits another change before (or without) `actuals-edit.js` running | Clear kept | The clear was **silently dropped** from the new revision | The hidden `ClearActualStart` rendered `false`; only the script set it from `data-official-start` | Medium: silent loss of proposal content, recoverable | Yes (correctness) | Render the flag from the pending item (`_EditActualsForm.cshtml`) |
| D3E-2 | HoD/Admin rejects a correction; the PO reopens the editor | The PO learns it was rejected, and why | The pending banner disappeared and the dates reverted, with no explanation; the remarks were invisible to the PO | No read model for the latest closed revision | Medium: essential workflow (resubmit after rejection) | Yes (the PO cannot act on a rejection) | `GetLatestRevisionAsync`, `ActualsEditorVm.LastRejection`: "Your correction revision N was rejected on … Reviewer's remarks: …" |
| D3E-3 | Official dates change after the PO submits (for example a HoD direct save) | The PO is told the update must be revised (plan G "D-3c": "Needs revision: official facts changed") | The editor still showed it as simply awaiting approval, while approval was impossible (SourceChanged) | The D-3c read model never compared `Before*` | Medium: the revision is stuck until the PO notices | Yes (plan requirement) | `ChangedSinceSubmission`, the same comparison approval uses, shown in the pending banner |

### 6a. Verified: a date typed over a pending clear, without JavaScript

**Concern** (cold review of `9191d426`).

1. A stage has a recorded start, and the PO's pending proposal clears it.
2. The PO reopens the editor without JavaScript. Since D3E-1, the hidden `ClearActualStart` renders as `true`.
3. The PO types a new start on that stage and submits.

The post therefore carries a date **and** a clear flag for the same stage.

**Traced behaviour:**

1. `EditActualsModel` passes both values unchanged into `ActualCorrectionProposalRow`.
2. `SubmitAsync` checks authority, the baseline and the tokens, then runs the shared `IActualTimelineValidationService`.
3. Its Editor projection refuses the combination explicitly: rule `ConflictingStartIntent`, "FS: Supply a start date or explicitly clear it, not both.". This is the same rule the direct path uses.
4. The outcome is `Invalid` before any write. The editor reopens with that message and "Nothing was submitted."

**Classification: verified correct; no production change.** No normalization prefers one value over the other:

- the typed date is not silently dropped;
- the clear is not silently applied;
- the pending revision is not superseded;
- no official fact or `StageChangeLog` is written.

`DateTypedOverAPendingClearWithoutJavaScript_IsRefusedAndThePendingRevisionIsKept` checks all of this against the rendered HTML: an exact before/after revision snapshot, the official FS start, and the history count. Case A (an untouched pending clear survives) is `ReopenedEditorWithoutJavaScript_KeepsAPendingClearAndBackfillItem`. Case C, where `actuals-edit.js` turns the flag to `false` when a date is typed over a pending clear, is covered in `actuals-edit-proposal.test.js`.

**Residual limitation (no JavaScript only).** The re-rendered form carries the pending clear again, so a user without JavaScript cannot replace a pending clear with a date on that stage. They can withdraw and resubmit, or use a browser with scripts enabled. This is recorded in §9.

One copy change was also made: the Decision Centre approval confirmation for Actual corrections now names the stages whose official dates will be published (observation 2).

## 7. Database, schema and migration verification

- **Model vs D-2 migration.**
  - The D-2 constraints are asserted on migrated PostgreSQL: the self-FK `Restrict`, the cascade from `Projects`, the item `Restrict`, the unique `(ProjectId, RevisionNo)` and `(RevisionId, StageCode)` indexes, the filtered one-`Pending` index, and the status, closure, entry-point, revision-number and clear-intent `CHECK`s. See `ActualCorrectionSchemaPostgresFixture` (23505/23514/23503 cases and purge).
  - A throwaway scaffold on the D-3e baseline produces **no** `ActualCorrection*` operation. It was run with `dotnet ef migrations add` and discarded; no file was committed.
- **Immutability.** There is no update path for items or submitted fields. The tests compare item snapshots before and after supersession, rejection and approval.
- **Lineage.** Both links are set atomically (C.4 step 9; PG forward-link failure rollback). Revision numbers stay gap-free after a rollback.
- **Decision metadata.** `ClosedByUserId`, `ClosedOn`, `DecisionNote` and `Status` are covered by the closure `CHECK` and the tests.
- **Audit.** `Projects.ActualCorrectionSubmitted`, `…Withdrawn`, `…Approved` and `…Rejected` are written only after commit.
- **Stage history.** One `StageChangeLog` per published stage, with action `ActualsUpdated` or `Backfill`, and the reviewer and revision in the note.
- **Purge.** `ProjectModerationService` clears the lineage links, then deletes items and revisions. This is tested on SQLite and PostgreSQL (D-2).
- **Startup boundary.** Startup applies migrations automatically and then validates the PostgreSQL catalogue (`DatabaseStartupMigrator`, `AutomaticMigrations=Mandatory`). This bears on K-13.

## 8. Release-blocker register

| Item | Finding | Blocks the coordinated release? | Smallest separate correction |
| --- | --- | --- | --- |
| **#2683** publish script | **Confirmed on `0277a1bb`.** The manifest check in `ops/publish/create-publish-folder.ps1` requires exactly 62 IDs ending in `20261201160000_…`. `Migrations/immutable-migration-ids.txt` has 119, ending in `20261217053003_AddActualCorrectionProposals`, so the script throws "Application migration manifest is incomplete" before publishing. | **Yes.** The release artifact cannot be produced with the documented procedure. | Compare the manifest with the migrations discovered in the built assembly instead of a fixed count. Rehearse on a clean Release build (offline SOP). |
| **#2684** snapshot drift | **Confirmed unchanged.** The scaffold adds `NotebookItemCollaborators` (which already exists), drops `IX_ProjectIdeaComments_ProjectIdeaId` and alters `ProjectBrief`. | No for D-2/D-3, which add no further migration. **Yes before any next scaffolded migration.** | Splice the snapshot, and add a CI "empty scaffold" check. |
| **#2682** `ProjectBrief` 2500/3000 | Pre-existing; unchanged by Phase D. | No for D-2/D-3. It is a separate user-visible defect. | Lower `ProjectBriefMaxLength` to 2500 (no migration), or widen the column only after #2684. |
| **K-12** topology | Unverified. | **Yes** (operational). | Operators must answer: number of app instances; background writers (workers, retention, OCR) that touch `Projects`/`ProjectStages`; whether a full outage is available. The plan assumes one outage window (I.1, R11: mixed binaries are not supported). |
| **K-13** migration execution | **Conflict found.** The code makes startup migration mandatory (`Database:ApplyMigrationsOnStartup=false` is ignored with a warning). `docs/deployment/offline-ws2022.md` says "Do not auto-apply migrations in production. Apply reviewed SQL scripts." | **Yes** (operational). | Operators decide which governs, and the losing document is corrected. If reviewed SQL is chosen, apply it before starting the new binary; startup then finds no pending migration. |
| **K-14** coordinated release | Preserved. | Process requirement. | R-D2 (migration `20261217053003`) and R-D3 ship in one maintenance window. R-D2 alone is only the emergency floor. |

## 9. Known accepted limitations

- No notifications on submit or decision (K-8 is optional). The PO sees outcomes in the editor (D3E-2).
- The Decision Centre detail preview runs the validator but not F.6. It warns when lifecycle updates are pending, and the decision enforces F.6 (observation 1 below).
- An editor post is the complete proposal. A pending item for a stage with no editor row blocks Submit; the PO can still withdraw.
- A stale or invalid post reloads the editor from current values; values the user typed are not kept.
- Without JavaScript, a pending start clear cannot be replaced by a date on the same stage. The post is refused (§6a), and the PO withdraws and resubmits, or enables scripts.
- Approval does not require an active project, matching direct HoD/Admin correction.
- R7 (Actual approval vs plan approval) relies on the shared project gate and is not separately tested.
- Accepted from #2680: a concurrent insert of a missing legacy IPA row can produce a handled unique conflict that needs a retry.

## 10. UX observations (for the separate Timeline UX discovery; no redesign here)

| Observation | Essential workflow? | Frequency | Recoverable? | Kind |
| --- | --- | --- | --- | --- |
| 1. Decision Centre readiness can show Ready while a pending lifecycle request will make approval refuse | No | Rare (needs a contradictory lifecycle request) | Yes: a clear refusal, nothing changed | Friction. The warning added in D-3d is sufficient; projecting F.6 in the preview would duplicate business validation |
| 2. Generic approval confirmation | No | Every approval | n/a | Copy, **improved** |
| 3. Users find the editor under Timeline → "Correct recorded dates" (offcanvas); the backfill modal is separate | Partly: two entry points for one concept | Common | Yes | Friction; discovery input |
| 4. A blank start in proposal mode means "keep the official value", so reverting a changed start needs the official date retyped (the row shows it) | No | Occasional | Yes | Friction |
| 5. Typed values are lost after a stale or invalid post | No | Occasional | Yes (re-enter) | Friction |
| 6. Editor and backfill use different interaction models (form post vs JSON modal) | No | Common | Yes | Consistency; discovery input |
| 7. The PO sees a rejection only in the editor, with no dashboard or notification | No (D3E-2 makes it findable) | Per rejection | Yes | Possible K-8 follow-up |

**Manual browser checklist** (run on a non-production copy before release):

1. As the assigned PO, open Timeline → correct recorded dates. Change one start, clear one Completed start, add a reason, and submit. Confirm the flash says "Official dates are unchanged".
2. Reopen. Confirm the "Revision 1 is awaiting…" banner, the prefilled values, the "Official: …" lines and the clear note. Change another stage and resubmit; confirm revision 2 keeps the clear.
3. Open the backfill modal. Submit a completion for an awaiting stage. Confirm the "submitted, not recorded" toast, and that the editor now lists both items.
4. As a HoD, open the Decision Centre. Confirm the "Actual date correction" row and the detail values (recorded vs proposed, "start cleared"). Approve, and confirm the dialog names the stages. Confirm the official dates and that the row has left the queue.
5. As the PO, submit again. As an Admin, reject with remarks. As the PO, reopen and confirm "Your correction revision N was rejected … Reviewer's remarks".
6. As the PO, submit. As a HoD, directly save the same stage. Confirm that the Decision Centre shows "Official dates changed…" with Approve disabled, and the PO banner says "Official dates changed for …".
7. Give the PO the HoD role, and confirm both buttons are disabled on their own revision.
8. Repeat step 1 with JavaScript disabled, and confirm the server-side behaviour is the same.

## 11. Rollback and recovery

These are unchanged from plan I.3.

- **Rolling back R-D3 to R-D2** is a binary swap. It is governance-safe because R-D2 refuses PO direct publication. Pending revisions stay inert, and on re-upgrade a changed baseline makes them SourceChanged.
- **Rolling back R-D2** requires restoring the pre-R-D2 database backup. The startup gate refuses older binaries once `20261217053003` is applied, and removing history rows is prohibited (`docs/engineering/migrations.md`).
- **The recovery rehearsal (I.3, J-11) has not been performed.** It must be done on a staging copy before release.

## 12. Outstanding decisions and requirements before deployment

1. Fix #2683 and rehearse packaging from a clean Release build.
2. Answer K-12 (topology, writers, outage) and K-13 (startup migration vs reviewed SQL), and correct the losing document.
3. Run the manual browser checklist (§10) on a staging copy.
4. Rehearse I.3 recovery (R-D3 → R-D2 and back) on staging.
5. Prepare release notes (J-14).
6. Obtain the owner's authorization for the coordinated R-D2 + R-D3 window (K-14).
7. Track #2682 and #2684 separately. #2684 must be fixed before the next scaffolded migration.
