# Phase D — coordinated D-2 + D-3 release: operational readiness and runbook

> **Executive status: Evidence incomplete — required rehearsal outstanding.** Operator decisions K-12 and K-13 are also unresolved. The code is merged and functionally accepted ([release-readiness report](phase-d-release-readiness.md)). The packaging blocker #2683 is closed, and a fresh publish dry run passed. Production deployment is **not authorized**.
>
> Production topology, database state and configuration were **not** inspected. Every production value below is either cited from the repository or marked **Unknown — operator confirmation required**.

Evidence keys used throughout:
- **V**: verified from code or configuration at `5ee16e9f`.
- **D**: documented but not independently verified.
- **O**: requires production operator confirmation.
- **C**: conflicting or unsupported.

## 0. Release baseline (verified)

`master` = `5ee16e9fe414a5932467ae2483f7545a37da427d`. It contains:

| Work | Merge |
| --- | --- |
| D-1a…D-1e, #2680 | `b5ef8030`, `07313590`, `307542b2`, `119d6cf3`, `8480a880`, `207acd82` |
| D-2 (#2681) | `e3e8ea85` |
| D-3a/b (#2685) | `736458c3` |
| D-3c (#2686) | `0720b037` |
| D-3d (#2687) | `0277a1bb` |
| #2689 publish-manifest fix (#2683 closed) | `8de714f8` |
| D-3e (#2688) | `5ee16e9f` |

Schema impact of the whole Phase D release: **one** application migration, `20261217053003_AddActualCorrectionProposals`, which is additive (§2.3). There is no Media Library migration in Phase D. K-14 (D-2 and D-3 in one window) remains documented in the [plan](phase-d-implementation-plan.md#approved-governance-for-d-2d-3).

> **Production may be behind more than one migration.** The release applies every migration between production's `__EFMigrationsHistory` and the manifest (119 IDs ending in `20261217053003`). The exact delta is **O**: run `ops/database/production-migration-inventory.sql` first.

---

## A. Production topology and writer inventory (K-12)

### A.1 Process and hosting facts

| Item | Finding | Key |
| --- | --- | --- |
| IIS hosting | ANCM v2, `hostingModel="inprocess"`, `processPath=".\ProjectManagement.exe"`, stdout logging off (`web.config`) | V |
| Instances / web garden | The repo sets no `maxProcesses`, overlapped-recycle or node configuration. The migrator anticipates concurrent instances ("Waiting for another application instance…", `DatabaseStartupMigrator`) | **O** |
| App pool / site | `ProjectManagementPool` (No Managed Code); site `D:\Sites\ProjectManagement\current` (`docs/deployment/offline-ws2022.md` C3) | D / O |
| Data Protection keys | App name `ProjectManagement_SDD`. The repository is `DP_KEYS_DIR`. If that is unset outside Development, the code falls back to `/var/pm/keys`, which on Windows is the drive-relative `\var\pm\keys` (`Program.cs`) | V (code) / **O** (value) |
| Health endpoint | **None implemented.** There is no `MapHealthChecks` or `/health` route; only a path exclusion in `ErpUsageMiddleware`. `offline-ws2022.md` C4/D1 and `production-checklist.md` ("/health/ready") rely on one | **C** (docs corrected in this PR; §C uses logs and SQL instead) |
| Startup order | (1) mandatory ApplicationDbContext migration and schema validation, which is fatal on failure. (2) MediaLibraryDbContext migration, which disables only media on failure. (3) Seeders only if `Database:RunSeedersOnStartup`. (4) **Unconditional ARPP IPA stage synchronization** (`SynchronizeAllAsync`). (5) `app.Run()`: hosted services start and requests are served | V |
| File roots | `PM_UPLOAD_ROOT` / `ProjectPhotos:StorageRoot`, `ProjectDocuments:Ocr:WorkRoot`, `DocRepo:RootPath`, media cache/model roots (`appsettings.json`) | V (keys) / **O** (values) |
| Native dependencies | `ocrmypdf` (external process; path validated if configured), optional LibreOffice, publication fonts (`wwwroot/fonts/publications/dm-sans`), SkiaSharp natives; checked by the publish script and `--compendium-offline-self-test` | V |
| External network | SMTP only (`SmtpEmailSender`); no runtime `HttpClient` use found | V / **O** (SMTP host is on the LAN?) |
| Backup / restore tooling | `ops/backup-db.ps1` (pg_dump custom format). `ops/restore-db.ps1` restores into a **separate staging database**, validates, renames atomically and **retains the previous database**; it needs an idle target (`docs/disaster-recovery.md` §2.4) | V |

### A.2 Hosted services (all in the web process)

No hosted service writes `ProjectStages`, `StageChangeLogs`, `StageChangeRequests` or the proposal tables, **except** `ProjectRetentionWorker`. It runs **immediately at start**, then daily, and hard-deletes trashed projects, including their stages, logs, revisions and items. The others (OCR, user/notification/audit retention, search, publication warmup, media) touch other tables. They stop with the process.

### A.3 Writers of the Phase D tables

"Gate" means `PlanRevisionConsistency.LockProjectAsync`. "Share" means the project row `FOR SHARE` (`ActualWriteAuthority`). "StageFU" means every stage row `FOR UPDATE` in `Id` order.

| Writer | Entry | Writes | Concurrency |
| --- | --- | --- | --- |
| `ActualCorrectionProposalService` (submit, withdraw, approve, reject) | editor, backfill, Decision Centre | proposal tables; on approval, `ProjectStages` Actual facts and `StageChangeLogs` | Gate + Share + StageFU (approve/submit); Gate + Share (reject/withdraw) |
| `StageActualsUpdateService`, `StageBackfillService` | HoD/Admin direct | Actual facts, logs | Gate + Share + StageFU |
| `StageDecisionService` → `StageProgressService` | lifecycle approvals | stages, logs, requests | Gate + StageFU (no Share) |
| `StageDirectApplyService` | HoD direct lifecycle | stages, logs, requests | Gate + StageFU (no Share) |
| `StageRequestService` | PO lifecycle requests | requests | Gate + StageFU |
| **`ArppIpaStageSynchronizer`** | **startup**, ARPP verify/reconcile | IPA stage rows, logs, requests | **No gate.** IPA rows `FOR UPDATE` (Id order); accepted design (plan E.3) |
| `HistoricalStageRecordService` | closed legacy projects | stages, logs | transaction only (plan F.5 row 7) |
| `ProjectFactsService.ClearBackfillFlagAsync` | Procurement edit | `RequiresBackfill` only | none (plan F.5 row 9) |
| Plan approval / EditPlan | plan pages | inserts `NotStarted` stages | Gate |
| Project create, legacy import | create page, admin import | new project and stages | none / transaction (new rows only) |
| AssignRoles, trash/restore, lifecycle | admin pages | `Projects` authority columns | row UPDATE lock (conflicts with Share) |
| `ProjectModerationService` purge (page and **retention worker**) | admin, worker | deletes all of the above, including proposals | transaction |

### A.4 Quiesce requirements during the upgrade

1. **No process of the old binary may run once the migration is applied.**
   - The old binary allows PO-only direct publication. That is the governance gap D-2 closes (K-14).
   - The old binary's purge does not delete proposal rows. A purge would then fail on the new `Restrict` foreign keys.
   - A *starting* old process refuses to start against the new schema (`EnsureNoUnknownAppliedMigrations`). An *already running* one keeps serving. Stopping every instance is therefore mandatory, not optional.
2. **Writers that run as soon as the new binary starts:**
   - the ARPP IPA sync, which is unconditional;
   - `ProjectRetentionWorker`;
   - the seeders, if enabled.

   These are expected and safe on the new binary. The release window must allow them to run before the site is reopened.
3. **External writers (psql scripts, reporting or ETL against `Projects` / `ProjectStages`): none found in the repo.** This is **O**.
4. A full stop covers all in-process writers: `app_offline.htm` alone stops the app, but stopping the **app pool** is required to guarantee no process remains. In-process hosting means one process per worker.

### A.5 Operator questions (K-12)

| # | Question | Repository answer |
| --- | --- | --- |
| 1 | How many production application instances or servers? | Unknown — operator confirmation required |
| 2 | Is a web garden (`maxProcesses` > 1) or overlapped recycle enabled? | Unknown — operator confirmation required |
| 3 | Any separate worker or console processes (scheduled tasks, `--backfill-forecast`)? | None in the repo; Unknown — operator confirmation required |
| 4 | Can anything outside PRISM write the database (scripts, ETL, reporting with write rights)? | None found; Unknown — operator confirmation required |
| 5 | Is a complete outage window available, and how long? | Unknown — operator confirmation required |
| 6 | Who is the deployment operator? | Unknown — operator confirmation required |
| 7 | Who owns backup and restore, and has `ops/restore-db.ps1` been exercised on this server? | Unknown — operator confirmation required |
| 8 | Production values of `DP_KEYS_DIR`, `PM_UPLOAD_ROOT`, OCR paths and `Database:RunSeedersOnStartup` | Unknown — operator confirmation required |
| 9 | Production's current migration head (`production-migration-inventory.sql`) | Unknown — operator confirmation required |

---

## B. K-13 migration-execution decision record

### B.1 Verified runtime behaviour (`Program.cs`, `Infrastructure/DatabaseStartupMigrator.cs`)

1. **Startup migration cannot be disabled.** `applyMigrationsOnStartup` is the constant `true`. `Database:ApplyMigrationsOnStartup` is read only to log "deprecated and ignored" when it is `false`. (`appsettings.json` has `false`; `appsettings.Production.json` has `true`. Both are ignored.) **V**
2. **Reviewed SQL can be applied before startup.** If `__EFMigrationsHistory` already contains every known migration, `pendingBefore` is empty: startup logs "is current", applies nothing, and continues to validation. **V**
3. **What startup always does**, under a session advisory lock `PRISM_ERP_EF_MIGRATIONS` (`pg_try_advisory_lock`, polling up to 10 minutes):
   - check the migration assembly against `Migrations/immutable-migration-ids.txt`;
   - refuse any applied migration unknown to the build;
   - apply what is pending;
   - verify closure;
   - run physical validation. This is read-only: `ApplicationDatabaseSchemaValidator`, the Notebook and DocRepo checks, and the search-vector check.

   The Media Library context runs the same steps through its own manifest, with a non-fatal failure. **V**
4. **On failure:**
   - any ApplicationDbContext migration or validation error aborts startup before requests are accepted (IIS returns 500.30). A diagnostic file is written through `StartupFailureReporter`;
   - a lock timeout throws, so startup fails. **V**
5. **Transactions.** EF applies each migration in its own transaction. The generated D-2 script is `START TRANSACTION … COMMIT` with idempotent guards and the history insert inside (verified by generating it at `5ee16e9f`). **V**
6. **Multiple instances** serialize on the advisory lock, so they do not race to migrate. However, the lock does not stop an *old* instance that is already serving traffic (§A.4). **V**
7. **Rollback by binary swap is not possible after the migration.** The old binary refuses unknown applied migrations, and deleting history rows is prohibited (`docs/engineering/migrations.md`). **V**
8. **Documentation conflict (C):**
   - `docs/deployment/offline-ws2022.md` A3/D1 says "Do not auto-apply migrations; apply reviewed SQL".
   - `docs/deployment/production-readiness.md` "Production deployment" relies on startup migration and log review.

   Both are current documents. **This PR does not choose between them.**

### B.2 Options

**Option A: apply reviewed SQL before starting the new binary.** Startup then finds nothing pending and only validates.

- **Compatibility:** works with the current code unchanged.
- **Prerequisites:**
  - Generate `dotnet ef migrations script <production-head> 20261217053003_AddActualCorrectionProposals --idempotent` from the **exact release commit**.
  - Review it and keep it with the release.
  - Apply it with `psql -v ON_ERROR_STOP=1 -f migration.sql` as the database-owner role, while the pool is stopped.
  - Run the same steps for `__EFMigrationsHistory_MediaLibrary` if that set has pending migrations (none for Phase D).
- **Failure:** the script is one transaction, so a failure leaves the schema unchanged. Fix and re-run (it is idempotent), or abandon the release.
- **Auditability:** highest. The reviewed SQL artifact, the `psql` transcript and the inventory before and after.
- **Partial-deployment risk:** low, provided the binary is swapped only after the script commits and no old process runs. If the old binary is started after the script, it refuses to start, which is safe but fails visibly.
- **Operator actions:** the most steps: generate, review, apply, re-inventory.
- **Changes needed:** none. A small documentation alignment only.

**Option B: let the new binary apply pending migrations at startup.**

- **Compatibility:** the current code's default path (`production-readiness.md`).
- **Prerequisites:**
  - The application role needs DDL rights. `production-checklist.md` assumes "application role has migration rights" (**O**).
  - Every instance must be stopped beforehand; one instance starts first.
  - The backup must already be verified.
- **Failure:** the migration transaction rolls back, startup aborts with 500.30, and the diagnostic file and logs record it. Then restore the old binary, which works because no migration was recorded, or fix forward.
- **Auditability:** good (startup logs list pending and applied migrations), but there is no pre-reviewed SQL artifact.
- **Partial-deployment risk:** low with a full stop. It rises if a second node starts the old binary concurrently.
- **Operator actions:** fewer steps.
- **Changes needed:** none.

**Option C (hybrid, supported by the repo): review the SQL, but let startup apply it.**

- Generate and review the idempotent script exactly as in A, as the audit and approval artifact. Do not run it by hand; start the new binary under Option B's prerequisites, and keep the reviewed script as evidence of what startup applied.
- Afterwards, verify with the inventory SQL that the recorded migrations match the reviewed script.
- **Compatibility:** no code change.
- **Auditability:** close to A.
- **Operator burden:** close to B.

### B.3 Recommendation (not approved)

**Recommended: Option A**, which keeps the existing offline SOP intact.

- It needs no source or configuration change.
- It gives a reviewed, transactional, idempotent artifact applied while nothing is running.
- It makes the binary swap a pure code change, so startup only validates.

**Option C** is the alternative if operators prefer not to run `psql` by hand.

Under either option, the operator must correct the losing document.

**This is an operator and owner decision (K-13).** No code or configuration was changed. If owners want startup to *never* apply migrations (refuse instead), that is a source change to `DatabaseStartupMigrator` and needs its own architectural decision and PR.

---

## C. Coordinated D-2 + D-3 release runbook (proposed until K-12 and K-13 are resolved)

**Placeholders the operator must fill in:**
- `<RELEASE_SHA>` (expected `5ee16e9f` or a later approved commit)
- `<DB_HOST>`, `<DB_NAME>`, `<OWNER_ROLE>`, `<BACKUP_DIR>`
- `<SITE_ROOT>` (`D:\Sites\ProjectManagement`)
- `<WINDOW>`, the operator name and the rollback owner

### C.1 Pre-release (before the window)

1. Confirm `<RELEASE_SHA>` is on `master`, and that its CI is green: Lint Views, Notebook Build and Tests, and the Production Database Migration Gate, with the PostgreSQL integration test **Passed 1, Skipped 0**.
2. On the build machine, run `.\ops\publish\create-publish-folder.ps1` at `<RELEASE_SHA>`. Keep its full output. It must end with "Publish folder created and validated" and Compendium `status: ok`.
3. Record the SHA-256 hashes of the published folder, for example with `Get-FileHash -Recurse`.
4. **Option A or C only:** generate the idempotent script from the production head (taken from the inventory) to `20261217053003`. Review it: additive DDL only for Phase D, with no `DROP` or `ALTER` of existing data. Have it approved.
5. Staging acceptance (§D.1) and the recovery rehearsal (§D.2) must be **passed**, with evidence attached.
6. Obtain written authorization for `<WINDOW>`, naming the operator and the rollback owner (K-14).

### C.2 In the window

| Step | Action | Evidence or check |
| --- | --- | --- |
| 1 | Announce the outage. Place `app_offline.htm` in `<SITE_ROOT>\current`. | Users see the offline page |
| 2 | **Stop the IIS app pool** on every node (K-12 #1–#2). | No `ProjectManagement.exe` / `w3wp` process for the site |
| 3 | Confirm no sessions remain: `SELECT pid, application_name, client_addr, state FROM pg_stat_activity WHERE datname = '<DB_NAME>' AND pid <> pg_backend_pid();` | Only expected admin sessions; no app connections and no external writers (K-12 #4) |
| 4 | Back up the database with `.\ops\backup-db.ps1`. Back up the files with `.\ops\backup-files.ps1` and copy `current` to `releases\previous`. | The dump exists. `pg_restore --list <dump>` succeeds. The recovery point is recorded (time, file, size) |
| 5 | Run `ops/database/production-migration-inventory.sql`. | Saved output: the pre-release head |
| 6 | **Option A:** `psql -v ON_ERROR_STOP=1 -h <DB_HOST> -U <OWNER_ROLE> -d <DB_NAME> -f migration.sql`. **Option B/C:** skip this step. | Option A: `COMMIT`, exit code 0, transcript saved |
| 7 | Replace `<SITE_ROOT>\current` with the **complete** validated publish output. Never copy individual files. Keep `appsettings.Production.json`, `DP_KEYS_DIR` and the data paths as configured. | The file hashes match C.1 step 3 |
| 8 | From `current`, run `ProjectManagement.exe --compendium-offline-self-test`. | `status: ok` |
| 9 | Start the app pool on **one** node only (K-12). Remove `app_offline.htm` for local access only, or keep the site bound to localhost until step 11. | — |
| 10 | Read the startup log. | "Migration preflight passed … Pending=0" (A) or "Applying 1 pending …" then "Automatic migration completed" (B/C). Then "Database startup gate completed … Latest applied migration: 20261217053003_AddActualCorrectionProposals", "Database deployment boundary completed successfully", and the media gate result. The ARPP sync line, if any. No `LogCritical` |
| 11 | Re-run the inventory SQL. | Head is `20261217053003`; count matches the manifest (119) |
| 12 | Smoke test (C.3), still restricted. | All pass |
| 13 | Release acceptance: the operator and the rollback owner sign off. **This is the last rollback point without data loss (§E).** | Recorded go |
| 14 | Start the remaining nodes, if any. Remove `app_offline.htm` and reopen. | — |
| 15 | Monitor for 24 h: startup and error logs, Decision Centre queue, `StageChangeLogs` volume, `PostCommitErrors` audit warnings, and the `Stages.ActualChronologyWarning*` log events. | No unexplained errors |

### C.3 Smoke tests (production, restricted, minimal and non-destructive)

1. Sign in as Admin. The home page, a project Overview and the Decision Centre load.
2. On a project, open **Correct recorded dates**. As HoD/Admin the editor shows **Save changes**. Do not save unless a real correction is due.
3. The Decision Centre's pending count loads, and an `ActualCorrection` row appears if one exists.
4. Document upload and OCR status pages load. Global search returns a known document.
5. Compendium preview generates.

Functional proposal tests (submit, approve, reject) belong on **staging** (§D.1), not production.

---

## D. Staging acceptance and recovery

**Preconditions:**
- A staging server built like production (IIS in-process, same PostgreSQL major version).
- A **restored copy of a recent production backup**, using `ops/restore-db.ps1` into a separate database.
- Production data never leaves the LAN.
- Test accounts: assigned PO, a second PO, HoD, Admin and a viewer.
- **Status: not executed.** No staging environment was accessible to this work.

### D.1 Manual browser acceptance

Each step passes only with the stated evidence: a screenshot or a database query result attached to the release record.

| # | Steps | Expected evidence |
| --- | --- | --- |
| 1 | As the assigned PO, open **Correct recorded dates**. Change a start, clear a Completed start, add a reason, and click **Submit for approval**. | Flash says "Submitted… Official dates are unchanged". `ProjectStages` unchanged; one `Pending` revision; no new `StageChangeLogs` |
| 2 | Reopen the editor. | "Revision 1 is awaiting…"; prefilled values; "Official: …" lines; the clear noted |
| 3 | Change another stage and resubmit. Then withdraw (accept the confirmation). | Revision 2 supersedes 1 (`SupersededByRevisionId` set). After withdrawal, status `Withdrawn`; facts unchanged |
| 4 | Submit again. Open the backfill modal and submit a missing completion. | Toast "submitted… not recorded". One `Pending` revision with both items; the backfill item has `Kind = Backfill` |
| 5 | As HoD, open the Decision Centre. | An "Actual date correction" row; detail shows Recorded vs Proposed and "start cleared" |
| 6 | Approve, reading the confirmation dialog. | The dialog names the stages. Official dates change; one `StageChangeLog` per stage with the HoD's `UserId` and the revision in `Note`; revision `Approved`; the row leaves the queue |
| 7 | As PO, submit; as Admin, reject with remarks. | PO editor: "Your correction revision N was rejected… Reviewer's remarks". No facts or logs changed |
| 8 | Give the PO the HoD role and open their own revision. | Approve and Reject are both disabled; "You submitted this correction" |
| 9 | Reassign the lead PO to the second PO. | The new PO sees the previous officer's pending or rejected revision attributed as "the previous Project Officer". The former PO cannot submit |
| 10 | As PO, submit; as HoD, directly save the same stage. | Decision Centre: "Official dates changed…" with Approve disabled. PO: "Official dates changed for …" and **Submit for approval** enabled. Resubmitting creates a new revision, which then approves |
| 11 | As PO, submit; as HoD, directly save exactly the proposed dates. | PO: "The official dates now match every date…"; withdrawal succeeds |
| 12 | Repeat step 1 with JavaScript disabled. | Same server behaviour. Typing a date over a pending clear is refused with "Supply a start date or explicitly clear it, not both" |

There is no browser-automation framework in the repository, and introducing one is out of scope.

### D.2 Recovery rehearsal (staging only)

| # | Action | Pass criterion |
| --- | --- | --- |
| R1 | Record the pre-release head with the inventory SQL. Take a backup. Note the time. | The dump lists cleanly |
| R2 | Run C.2 steps 1–11 with the chosen K-13 option. | Head is `20261217053003`; startup gate log lines as in C.2 step 10 |
| R3 | **Interrupted rollout.** Before step 7, start the *old* binary against the migrated database. | The old binary refuses to start (unknown applied migration). This is evidence that a binary-only rollback is impossible after migrating |
| R4 | **Failed migration.** On a scratch copy, make the script fail (for example, pre-create `ActualCorrectionRevisions`), then apply it with `ON_ERROR_STOP=1`. | The transaction rolls back; schema and history unchanged; the old binary still starts |
| R5 | Run D.1 steps 1–7 to create D-3 data. | Revisions, items and logs exist |
| R6 | **Restore after D-3 data.** Run `ops/restore-db.ps1 -DumpFile <R1 dump>` with the site stopped, then start the **old** binary. | The old binary starts. D-3 rows written after R1 are **gone**, which is the expected loss. The retained previous database still holds them |
| R7 | Re-upgrade (R2). Check integrity. | `StageChangeLogs` count = official changes; no orphan items; one `Pending` per project at most |
| R8 | Time R1–R7. | The duration is evidence for the outage window (K-12 #5) |

**Go/no-go evidence for the release:** R1–R8 passed with logs and query output attached.

---

## E. Rollback strategy

| Situation | Action | Database restore? | Data loss |
| --- | --- | --- | --- |
| **E1.** Failure before the migration is applied (C.2 steps 1–5) | Restore `releases\previous` (if replaced), start the pool, remove `app_offline.htm` | No | None |
| **E2.** The migration fails (transaction rolled back) | Same as E1. The old binary starts because no migration was recorded | No | None |
| **E3.** Migration applied, new binary fails startup or smoke tests, **site not reopened** (before C.2 step 13) | Either fix forward, or restore the C.2 step 4 backup with `ops/restore-db.ps1` (separate database, atomic rename, previous retained), then the old binary | **Yes** (the old binary refuses the new schema) | Nothing, if no user writes occurred after step 4. The site was closed |
| **E4.** Partially replaced package (copy interrupted) | Complete the copy from the validated publish folder and verify the hashes. Never run a mixed folder | No | None |
| **E5.** After reopening, with new D-3 records written | **Prefer fix forward** (an R-D3 → R-D2 binary swap is only safe if it is the D-2 build, plan I.3). Rolling back to the pre-D-2 binary requires a **database restore**, which loses every write after the recovery point, including proposals, decisions and direct corrections | Yes | **All post-backup writes**: must be accepted by the owner |

**Decision points:**
- C.2 step 6 (after the migration).
- C.2 step 13 (acceptance before reopening): the last point with no data loss.
- Any time after reopening (E5).

**Owner:** the named rollback owner decides at each point. The operator executes. The restore is performed by whoever owns the backup (K-12 #7).

Arbitrary down-migrations are **not** supported. Removing history rows is prohibited.

---

## F. Release blocker and decision register

| ID | Status | Blocks the release? | Evidence and next action |
| --- | --- | --- | --- |
| **#2683** publish manifest | **Closed** by #2689 (`8de714f8`) | No | Fresh dry run at `5ee16e9f` passed: 119 ordered IDs; source parity; published manifest byte-identical; Media manifest present; web.config limit; Compendium self-test `ok`. The `.sh` variant has no manifest checks and is not the documented Windows path |
| **K-12** topology | **Open** | **Yes** | §A.5 questions 1–9 |
| **K-13** migration execution | **Open (decision record §B)** | **Yes** | Owners choose A, B or C. The losing deployment document is corrected |
| **K-14** coordinated release | Preserved | Process | §C; one window; no old process alongside the new schema |
| Staging browser acceptance | **Not executed** | **Yes** | §D.1 |
| Staging recovery rehearsal (plan I.3, J-11) | **Not executed** | **Yes** | §D.2 |
| Release notes (J-14) | Not prepared | **Yes** | They must list the PO workflow change, the Decision Centre type and the migration |
| Health endpoint documented but absent | Confirmed (**C**) | No (runbook uses logs and SQL) | `offline-ws2022.md` and `production-checklist.md` corrected in this PR; implementing an endpoint is optional separate work |
| **#2684** snapshot drift | Open | No for this release (no new migration) | Re-verified at `5ee16e9f` with a throwaway scaffold (discarded): adds `NotebookItemCollaborators`, drops `IX_ProjectIdeaComments_ProjectIdeaId`, alters `ProjectBrief`; nothing for proposals. There is no runtime risk. Fix in a **separate PR before the next scaffolded migration**: splice the snapshot, then decide on the redundant index |
| **#2682** `ProjectBrief` 2500/3000 | Open | No (pre-existing; unchanged) | A brief of 2501–3000 characters fails to save. Smallest fix: lower `ProjectBriefMaxLength` to 2500 (no migration). Priority: next maintenance release |
| `SixLabors.ImageSharp` 3.1.11 advisories (NU1902/NU1903 in the publish output) | Observation | Not a Phase D blocker | A pre-existing dependency; owners should assess separately for the offline LAN |

## G. Go/no-go checklist (none satisfied by this PR unless marked)

- [x] Merged code verified on `master` (`5ee16e9f`; §0).
- [ ] Exact `<RELEASE_SHA>` approved.
- [ ] CI green on `<RELEASE_SHA>`, with PostgreSQL Passed 1, Skipped 0. Satisfied for `5ee16e9f` by the #2688 runs, but must be re-confirmed for any later SHA.
- [ ] Package integrity: publish script output and hashes for `<RELEASE_SHA>`. A dry run passed for `5ee16e9f` on a development machine, not the release build machine.
- [ ] K-12 answered (§A.5).
- [ ] K-13 decided, and the losing document corrected.
- [ ] Maintenance procedure (§C) approved, with placeholders filled in.
- [ ] All writers quiescible: every node stoppable, no external writers.
- [ ] Backup taken and restore capability proven (§D.2 R6).
- [ ] Staging browser acceptance passed (§D.1).
- [ ] Staging recovery rehearsal passed (§D.2).
- [ ] Release notes prepared.
- [ ] Operator and rollback owner named.
- [ ] The coordinated D-2 + D-3 window is authorized (K-14).
