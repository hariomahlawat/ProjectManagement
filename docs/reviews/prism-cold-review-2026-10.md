# PRISM ERP — fresh current-master cold review

**Artifact status:** This is an independent code-review artifact, not production documentation or an operator runbook. Findings require individual revalidation against the code and deployment state during remediation; no fixes are included.

- **Reviewed branch:** `master`.
- **Exact reviewed SHA:** `332075ea036085b54e24875b9cc14c974e6f2361`.
- **Fetched remote:** `origin/master` resolved to the same SHA; checkout was explicitly reset to this commit before inspection.
- **Git status:** `?? docs/reviews/`; no tracked modifications, before inspection and again immediately before report replacement. This directory contained the previous untracked report. Replacing this report is the only review-authored repository change.
- **Root AGENTS.md found:** **Yes**, read before current-code review. No other applicable nested AGENTS.md was found.
- **Current workflow trigger summary:** Notebook: pushes and PRs targeting `master`; Lint Views: pushes and PRs targeting `master`; Production Database Migration Gate: all pushes and PRs; replacement-ZIP workflow: path-filtered pushes plus manual dispatch. Details below.

Review date: **14 October 2026, Asia/Calcutta**. Repository root: `E:\Dot Net Web Development\ProjectManagement`. All finding locations below are exact current repository paths, with absolute clickable targets. Line ranges refer to the reviewed SHA.

This document replaces the review of `2e26b421ada046534bc5f33a3f224645d61d46a5`. That stale report is not evidence for any current finding.

## A. Executive summary

Current master has improved repository guidance, correct master lint triggers, and a substantive test-host isolation fix. The previous Data Protection configuration-override finding has disappeared: startup now resolves the final IConfiguration, and a new regression exercises persistent test keys in both Development and Production. Endpoint fixtures also disable OCR and relational SearchV2 work. These changes must not be confused with a passing production-host validation or with security fixes to the application endpoints.

The fresh review still identifies material recovery, security, workflow and asynchronous-integrity defects. Additional current-code findings include incorrect automatic realignment semantics, lifecycle/audit split commits, a notification failure path that can consume a dispatch without creating a notification, unsafe full-search-generation activation, and an orphaned replacement-packaging workflow.

**No Critical finding was established.** Repair priorities are recovery safety and durable storage, immutable/atomic planning decisions and commercial facts, mandatory account/request enforcement, and notification/search convergence. No architectural rewrite is necessary.

### Method and actual validation

1. Fetched origin/master and reset the tracked checkout to the explicitly requested SHA. Confirmed branch, HEAD, remote SHA and status before reviewing.
2. Read root `AGENTS.md`, root README and current `docs/README.md`, `current-state.md`, `engineering-invariants.md`, `build-and-test.md`, production-readiness/checklist, offline WS2022 deployment and migration policy before deriving conclusions. The executable implementation outranks documentation assertions.
3. Searched broadly, then traced selected endpoint/service/worker/model/migration/test paths. Independent reviewers received only the exact current SHA and current-source task; they did not read the stale report. The coordinator subsequently checked material call paths directly.
4. Only after the new source findings were assembled, read the old report's finding headings to construct the explicit disposition table. No prior conclusion was accepted without current evidence.
5. **Executed:** `node tools/run-js-tests.js` — **812 tests passed, 0 failed, 0 skipped**, exit 0. This uses the existing local Node dependency installation; it was not a fresh `npm ci`. Safe in-memory PowerShell reproduced the `$Host` binding failure. Read-only ripgrep reproduced guard regex exit 2 and failure to match the current ARPP event attributes. A local Git-Bash guard invocation returned exit 1 without usable diagnostics; it is not represented as a reliable Unix CI reproduction.
6. **Not executed:** dependency restore, .NET build/full suite, regenerated Notebook asset check, PostgreSQL migrations/race tests, publish, real IIS host, SMTP, destructive restore or hostile decoder payload. Build/asset commands can change committed generated files; operator/exploit checks require isolated infrastructure. Existing build/obj outputs were not treated as proof of the current commit.

**Confidence meaning:** Confirmed establishes a deterministic source defect or a stated safe reproduction. High confidence identifies a fully traced failure interleaving needing provider/runtime regression. Needs verification means an important policy, deployment or decoder prerequisite remains unresolved. These labels do not imply every issue was exploited on a running server.

Review breadth covers the requested categories through current-source searches and selected deep traces, not an exhaustive audit of every handler, report cell or native library. No cloud dependencies, microservices, SPA rewrite or generic EF repository are proposed. Offline LAN is the deployment constraint, never a security exemption.

## B. Current CI baseline

| Workflow and current triggers | Expected executable gates | What its result does not establish |
| --- | --- | --- |
| [.github/workflows/notebook.yml](<E:/Dot Net Web Development/ProjectManagement/.github/workflows/notebook.yml>) — push/PR to `master` | Ubuntu; Node 22; npm ci; repository JS suite; Notebook regenerated-asset diff check; .NET 8 restore, Debug build and solution tests. | Windows/IIS production deployment, offline complete payload, or actual Search PostgreSQL execution (F12). The name understates broad solution testing. |
| [.github/workflows/lint.yml](<E:/Dot Net Web Development/ProjectManagement/.github/workflows/lint.yml>) — push/PR to `master` | Ubuntu, checkout v4, `tools/check-views.sh`. | Two guard checks are ineffective, and the surviving style ban disagrees with existing markup (F09). The old main-only trigger and checkout-v3 observation are fixed. |
| [.github/workflows/database-migrations.yml](<E:/Dot Net Web Development/ProjectManagement/.github/workflows/database-migrations.yml>) — all pushes/PRs | PostgreSQL 16 service; Node/.NET restores; Release build; application/media immutable migration metadata tests; guarded PostgreSQL migration integration test. | Full Release application suite, production web host, search concurrency or Windows package validation. It supplies migration-test variables, not `PRISM_SEARCHV2_TEST_CONNECTION`. |
| [.github/workflows/build-project-tot-v3-zip.yml](<E:/Dot Net Web Development/ProjectManagement/.github/workflows/build-project-tot-v3-zip.yml>) — replacement-path/workflow-file pushes; workflow_dispatch | Attempts Python generator, ZIP, commit and push with contents-write permission. | Its generator is absent from this commit, so it cannot perform its stated manual packaging function (F13). It is not an ERP release gate. |

**Trust assessment:** current definitions provide meaningful normal tests and a genuine PostgreSQL migration check. The executed JS suite is green locally. There is still no basis to call master a fully trustworthy production release baseline: F09/F11/F12 and missing Windows/disconnected publish validation leave material gaps. Actual GitHub run status and branch-protection requirements were not queried; this review does not claim current remote CI is green or red.

### Current test-host configuration — explicitly rechecked

[PrismTestWebHostBuilderExtensions.cs](<E:/Dot Net Web Development/ProjectManagement/ProjectManagement.Tests/Infrastructure/PrismTestWebHostBuilderExtensions.cs:32>) assigns unique application/media InMemory stores, a unique temporary `DP_KEYS_DIR`, disabled OCR/backfill/SearchV2 options, and removes Media Library plus all three OCR hosted workers. It replaces generic DbContext provider configuration as well as the context/options registrations. `[PrismTestOcrStartupTests.cs](<E:/Dot Net Web Development/ProjectManagement/ProjectManagement.Tests/Infrastructure/PrismTestOcrStartupTests.cs:19>) exercises both environments, inspects the actual key repository, protects/unprotects a value and checks key XML persistence; it retains production secure-cookie assertions and tests that real OCR path validation still rejects a missing configured executable.

`Program.cs:138–163` now configures KeyManagementOptions from final IConfiguration, after test overrides are composed. **The previous R09 cause is fixed.** These .NET tests were inspected, not executed in this pass. Endpoint InMemory tests intentionally do not prove real relational index/lock behavior; disabling unrelated workers is appropriate isolation, not weakening production validation.

## C. Ranked priorities

| Requested ranking | Current findings | Main impact |
| --- | --- | --- |
| 1. Data loss/integrity | F02, R07, F06, R01, R02, R11, F07, R12 | Destructive restore, unbacked uploads, approval of unseen plans, contradictory authority, duplicate facts, incorrect realignment, invalid chronology and incomplete audit. |
| 2. Security bypass | F03, F05; V01/V03 conditional | API bypass of password rotation, same-site CSRF, qualified decoder/proxy exposure. |
| 3. Silent operational failure | F01, F08, R06, R08, R13, R05 | Missing backups, suppressed resubmission alerts, missing outbox events, native false success, consumed notification work and pending media without a job. |
| 4. Deployment/startup | V02, V04, F13 | Unsafe stock configuration, contradictory operator instructions, broken manual package job. |
| 5. Reliability/concurrency | R03, R14, R04, F04, R10 | Unfenced search attempts/generation swaps, media snapshot ordering, shared login budget, optional SMTP lifetime. |
| 6. Validation/performance/maintainability | F09–F12; observations | Unreliable guards, CSP-blocked controls, test dependency drift and silent integration no-ops. No measured performance defect was established. |

## D. Bounded areas with no additional material finding

- Inspected published-project read access is intentionally broad for authenticated users through `ProjectAccessGuard`. Cross-project published reads were not mislabeled IDOR. Inspected project document/photo writes/streams check relevant project/resource relationships and access before opening content.
- Notebook uses owner/collaborator access levels and antiforgery-protected controllers. Administrative pages inspected have capability policies. Disable/delete lifecycle uses permanent lockout and security-stamp updates. New usage middleware records authenticated successful HTML navigation and isolates recorder failures.
- Protected file tokens bind expiry and normally user identity; project document preview checks document/file-stamp/publication/archive state. No attacker-controlled protected-token issuance path established a new traversal finding. Generic helper weaknesses alone were not promoted into exploits.
- Project, Visit and Social Media photo services detect actual format. Their allowed MIME checks reject detected TIFF despite a misleading caller MIME. No simple upload MIME-spoof route to TIFF decoding was established.
- Media worker completion has ownership checks and cache/classification invalidation tokens. PRISM full catalogue reconciliation obtains its advisory lock before loading source data. Activity media producer events are attached through the registered SaveChanges interceptor. R04 is a narrower targeted-upsert snapshot race.
- Sampled OCR paths honor cancellation; project OCR row-version checks mitigate stale writes. Multiple instances may duplicate expensive OCR, but this pass did not establish a separate data-resurrection bug from that observation.
- Notification unique source-dispatch and recipient/fingerprint constraints exist; SignalR failure leaves already-durable notifications available for polling. These safeguards do not repair the pre-insert failure path R13 or producer problems F08/R06.
- Migration manifests/discovery, PostgreSQL advisory-lock startup coordination, physical stage/search-vector validation and guarded real migration tests are present. The migration test checks a `prism_test_` empty database, both lineages and legacy constraint drift. No migration-from-zero failure was freshly established; those tests were not run here. Future-dated IDs are documented and are not themselves defects.
- Sampled ARPP-controlled IPA facts, canonical formal-update/positive PNC-then-L1 resolution, assigned-PO actual-date/backfill authorization and atomic stage-decision claims have concrete safeguards. No new inconsistency was found in those particular paths.
- Searches of active Razor markup/runtime services did not establish required public CDN/font/script/model fetches, mandatory SaaS identity, telemetry or cloud APIs. LibMan/package acquisition and library namespace/source-map URLs are build/static metadata, not runtime Internet dependencies. Local model checksums and the pre-host Compendium offline self-test are present. Optional SMTP can be a LAN service.
- Parameterized search SQL was inspected; no SQL-injection path was established. Search V1/V2 semantic equivalence and every export formula remain unproven, rather than being assumed correct or defective.

## Disposition of the stale report — independently revalidated

This table is a comparison index only; evidence is in the current findings below.

| Previous ID/observation | Current disposition |
| --- | --- |
| R09 — ignored test `DP_KEYS_DIR` override | **FIXED / DISAPPEARED.** Program now reads final IConfiguration and the new startup regression verifies the repository and persisted keys. Not an active finding. |
| F09 — lint push targeted main; checkout v3 | **FIXED portions.** Both push and PR now target master; checkout v4. Regex/error-handling defects remain, freshly reproduced, under F09. |
| Root phase README / missing agent guidance observations | **RESOLVED observations.** Root README and AGENTS now provide current orientation; historical cleanup is documented. They are not current defects. |
| F01/F02 | Still present after fresh script reads and safe binding reproduction. |
| F03/F04/F05 | Still present after current endpoint/filter/limiter tracing. F04 is assessed Medium here: new sign-ins are denied; established sessions are not shown to terminate. |
| F06/F07/F08 | Still present after current form, chronology and fingerprint traces. |
| F09/F10/F11/F12 | Remaining implementation/validation defects still present; corrected triggers are not carried forward as defects. |
| R01/R02 | Still present in current transition logic/index configuration. Fact row-version protection does not serialize first creation of a missing row. |
| R03/R04/R05 | Current claim/snapshot/split-commit paths still establish these risks. New generation-activation risk R14 is separately evidenced. |
| R06/R07/R08/R10 | Current post-commit producer, fallback root, native exit and SMTP lifetime paths still establish these risks. |
| V01/V02/V03/V04 | Remain qualified verification items; none is promoted to a proved deployed exploit. Current production-readiness prose claims credential removal and key protection not established by current executable configuration. |
| Earlier statement that no extra notification dispatcher defect was found | **WITHDRAWN as assurance.** Fresh inspection establishes R13; existing duplicate/SignalR protections do not cover it. |

Newly derived findings not in the stale report: F13 and R11–R14. Their absence from an earlier list had no evidentiary weight.

## 1. Confirmed defects

### F01 — Windows database backup/restore parameter binding fails

**Severity:** High. **Confidence:** Confirmed. **Category:** Operator scripts / recovery.

**Location/symbol:** [ops/backup-db.ps1](<E:/Dot Net Web Development/ProjectManagement/ops/backup-db.ps1:9>), script `param`, 9–17; [ops/restore-db.ps1](<E:/Dot Net Web Development/ProjectManagement/ops/restore-db.ps1:9>), `param`, 9–16.

**Evidence:** both assign `[string]$Host`; PowerShell's case-insensitive automatic `$Host` is read-only. Current safe scriptblock reproduction failed with `Cannot overwrite variable Host because it is read-only or constant.` No database utility was run.

**Scenario/impact:** scheduled backup or emergency restore fails before its intended native command, leaving operators without the documented Windows recovery path.

**Minimal remediation:** rename to `PgHost`, update uses, optionally preserve `Host` as an alias. **Regression:** default and explicit argument invocation against harmless stubs must reach the expected utility. **Coverage:** no script-binding regression found. Include R08 before treating a repaired script as operationally safe.

### F02 — Restore destroys the database before validating restoration

**Severity:** High. **Confidence:** Confirmed. **Category:** Data loss / restore sequencing.

**Location/symbol:** [ops/restore-db.sh](<E:/Dot Net Web Development/ProjectManagement/ops/restore-db.sh:15>), validation/drop/restore, 15–40; [ops/restore-db.ps1](<E:/Dot Net Web Development/ProjectManagement/ops/restore-db.ps1:19>), same sections, 19–58.

**Evidence:** file/path existence is checked, then `dropdb`/`createdb` run before reading the archive. PowerShell only checks `pg_restore.exe` after the drop. Bash `set -e` cannot restore an already-dropped original.

**Scenario/impact:** truncated/wrong archive, missing restore tool or restore SQL failure destroys the valid original and leaves an unusable replacement. Windows's sequence is currently masked by F01; Bash is independently executable.

**Minimal remediation:** preflight every utility/archive/target and restore to a replacement database; validate before deliberate cutover while retaining the original. A listing alone cannot guarantee a successful restore. **Regression:** corrupt archive, missing utility and failed SQL must preserve original data and fail. **Coverage:** no recovery drill/integration test found; reproduce only on disposable targets.

### F03 — Mandatory password change does not restrict API/controller/hub use

**Severity:** Medium. **Confidence:** Confirmed. **Category:** Authentication/account-state enforcement.

**Location/symbol:** [Infrastructure/EnforcePasswordChangeFilter.cs](<E:/Dot Net Web Development/ProjectManagement/Infrastructure/EnforcePasswordChangeFilter.cs:8>), `EnforcePasswordChangeFilter`, 8–35; [Program.cs](<E:/Dot Net Web Development/ProjectManagement/Program.cs:945>), registration 945/994 and endpoint composition 1352–1360; `LoginModel.OnPostAsync`.

**Evidence:** enforcement is only `IAsyncPageFilter`; current flag references reveal no shared middleware/authorization/hub boundary. Login issues ordinary authentication. Controller, minimal API and hub policies do not check `MustChangePassword`.

**Scenario/impact:** a reset privileged account uses its temporary password to sign in, then calls archive or other permitted APIs without rotating it. Page redirects do not restrict those privileges; sliding cookies can keep API access active.

**Minimal remediation:** shared authenticated-request restriction with narrow login/logout/change-password exceptions and suitable API/realtime errors. **Regression:** real flagged Identity cookie across page/controller/minimal/hub, then successful rotation restores access. **Coverage:** `UserAccountStateResolverTests` intentionally permits sign-in; landing/source tests do not verify restricted API usage.

### F04 — Anonymous requests exhaust a shared login quota

**Severity:** Medium. **Confidence:** Confirmed. **Category:** Authentication availability.

**Location/symbol:** [Program.cs](<E:/Dot Net Web Development/ProjectManagement/Program.cs:389>), `AddFixedWindowLimiter("login")`, 389–397; [Areas/Identity/Pages/Account/Login.cshtml.cs](<E:/Dot Net Web Development/ProjectManagement/Areas/Identity/Pages/Account/Login.cshtml.cs:17>), page-level limiter, 17–20 and GET at 58.

**Evidence:** one named unpartitioned bucket permits ten requests/five minutes. Page metadata applies to anonymous GET as well as credential POST.

**Scenario/impact:** one LAN client sends ten page GETs each window; other clients cannot obtain new sign-ins until renewal. Normal concurrent use can also exhaust it. Existing sessions are not shown to terminate.

**Minimal remediation:** partition trustworthy credential-attempt limits and exempt display GETs; retain per-account Identity lockout. Resolve V03 before trusting forwarded IP. **Regression:** exhausted client A cannot block B; GET does not consume attempt quota. **Coverage:** no current rate-limit isolation test found.

### F05 — Bodyless minimal mutations omit antiforgery

**Severity:** Medium. **Confidence:** Confirmed omission; same-site exploit prerequisite. **Category:** CSRF/request integrity.

**Location/symbol:** [Program.cs](<E:/Dot Net Web Development/ProjectManagement/Program.cs:1756>), archive 1756–1775, restore-archive 1777–1796; bodyless task creation 2570–2598; `ProjectModerationService.ArchiveAsync`.

**Evidence:** these cookie-authenticated POSTs mutate state without token validation/required-antiforgery metadata. `UseAntiforgery` at 1355 and header copying at 1334–1348 do not protect bodyless handlers automatically.

**Scenario/impact:** Admin/HoD visits an attacker-controlled HTTPS page on another port of the ERP hostname or a same-site sibling domain. A plain HTML form sends the Strict same-site cookie to an archive POST and attributes mutation to the victim. **Ordinary unrelated cross-site forms are mitigated by Strict cookies**; no universal cross-site claim.

**Minimal remediation:** enforce antiforgery before cookie-authenticated unsafe minimal actions using a shared boundary. **Regression:** actual role-authorized cookies with missing/invalid token fail without changes; valid canonical/legacy token succeeds. **Coverage:** service moderation tests and tokenless custom-auth API tests do not establish production cookie-CSRF protection; other endpoints have explicit protection.

### F06 — Overview approves the current submission instead of reviewed content

**Severity:** High. **Confidence:** Confirmed. **Category:** Approval integrity/stale form.

**Location/symbol:** [Pages/Projects/Timeline/_ReviewPlan.cshtml](<E:/Dot Net Web Development/ProjectManagement/Pages/Projects/Timeline/_ReviewPlan.cshtml:124>), form 124–132; [Review.cshtml.cs](<E:/Dot Net Web Development/ProjectManagement/Pages/Projects/Timeline/Review.cshtml.cs:45>), `InputModel`/`OnPostAsync`, 45–49, 79/95; `PlanApprovalService.ApproveLatestDraftAsync`/`RejectLatestPendingAsync`.

**Evidence:** live Overview form posts ProjectId/decision/note with no reviewed submission identity; handler chooses latest pending. Exact-version Decision Centre methods exist at `ApprovalDecisionService:237–249` but are not used here.

**Scenario/impact:** reviewer opens A; another rejects it; owner changes/resubmits A or submits B; old tab now approves unseen dates and attributes approval to the first reviewer. Rejection has the equivalent targeting problem.

**Minimal remediation:** bind reviewed version plus immutable submission revision/token and atomically validate/claim it. ID alone cannot protect reuse. **Regression:** reject/edit/resubmit followed by stale Overview POST conflicts without activation. **Coverage:** existing self-approval/Decision Centre protections do not cover this active form.

### F07 — Predecessor correction does not validate existing successor dates

**Severity:** Medium. **Confidence:** Confirmed. **Category:** Workflow chronology.

**Location/symbol:** [Services/Stages/StageActualsUpdateService.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Stages/StageActualsUpdateService.cs:108>), `UpdateAsync`, 108–132, 143–207, persistence near 295; `Timeline/EditActuals.cshtml.cs:69–89`.

**Evidence:** projected completion state includes all stages, but validation iterates submitted rows and checks only each one's predecessor. It omits untouched dependent successors.

**Scenario/impact:** in V1, FS completed January 10, IPA started January 11. Correct only FS completion to January 20 with valid past dates and no pending request. FS saves; IPA remains before its predecessor completion, violating chronology enforced when IPA is edited.

**Minimal remediation:** validate affected successor closure against the whole proposed state, preserving coherent joint corrections/same-day semantics. **Regression:** predecessor-only conflicting correction fails; coherent batch succeeds. **Coverage:** `StageActualsUpdateServiceTests:392` covers the reverse edit direction and `:321` same-day behavior, not this correction.

### F08 — Resubmission reuses notification fingerprint

**Severity:** Medium. **Confidence:** Confirmed. **Category:** Notification event identity.

**Location/symbol:** [Services/Plans/PlanNotificationService.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Plans/PlanNotificationService.cs:217>), `BuildMetadata`, 217–248; [NotificationDispatcher.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Notifications/NotificationDispatcher.cs:299>), `FindExistingNotificationAsync`, 299–322; notification uniqueness in `ApplicationDbContext:1883–1903`.

**Evidence:** fingerprint is `plan:{id}:{eventType}`. Rejection returns that same PlanVersion to Draft; resubmission reuses ID. Recipient/fingerprint lookup and uniqueness suppress a later distinct event as already delivered.

**Scenario/impact:** HoD reads/rejects a notice, PO edits/resubmits while it is retained, and no fresh unread notice is created. Repeated rejection alerts are similarly suppressed. Suppression lasts while the prior matching notification exists, not necessarily forever after retention deletion.

**Minimal remediation:** persisted immutable submission/event revision in deduplication identity, stable for retry of that event. **Regression:** two real submit/reject cycles yield distinct notifications; duplicate retry of either yields one. **Coverage:** PlanNotificationServiceTests mock publisher and check metadata/recipients, not lifecycle deduplication.

### F09 — View guard errors fail open and remaining rule conflicts with markup

**Severity:** Medium. **Confidence:** Confirmed. **Category:** CI guard fidelity.

**Location/symbol:** [tools/check-views.sh](<E:/Dot Net Web Development/ProjectManagement/tools/check-views.sh:13>), `run_check`/patterns 13–43; `.github/workflows/lint.yml`.

**Evidence:** ordinary rg rejects lookahead, reproduced as exit 2; the script hides stderr and ignores unsuccessful conditional commands. Double-escaped word/whitespace forms in single-quoted Bash patterns fail to match intended event attributes; current ARPP `onclick` returned no match. The independent `style="` rule does match existing views, e.g. `ProjectPulse/_Widget.cshtml:340,394` and IPR counters. Thus two checks can fail open while the full job can fail on normal current markup.

**Scenario/impact:** prohibited inline scripts/handlers evade checks; valid product styles repeatedly break a now-master-triggered gate, encouraging disregard of its signal.

**Minimal remediation:** supported properly escaped patterns/parser; distinguish no-match from tool error and fail errors; align style rule to intended current CSP policy. **Regression:** tiny valid/invalid fixtures and missing-tool/regex-error cases. **Coverage:** no executable guard fixture suite found. **Already fixed:** master triggers and checkout v4; neither remains a finding.

### F10 — ARPP Print controls are blocked by current CSP

**Severity:** Low. **Confidence:** Confirmed. **Category:** Reporting UI.

**Location/symbol:** [Areas/ProjectOfficeReports/Pages/ARPP/Print.cshtml](<E:/Dot Net Web Development/ProjectManagement/Areas/ProjectOfficeReports/Pages/ARPP/Print.cshtml:75>), toolbar 75–77; Program normal-page CSP; `PrintModel.OnGetAsync`.

**Evidence:** buttons use inline `onclick` for print/close; the page has no external binding, and its response gets `script-src 'self'` without an inline-handler exception.

**Scenario/impact:** authorized user clicks Print / Save PDF or Close and browser blocks it. Keyboard/browser printing remains possible, so this is not export data loss.

**Minimal remediation:** local external script bindings, retaining CSP. **Regression:** real CSP browser test invokes controls without violation. **Coverage:** ARPP print markup/CSS and authorization contracts do not exercise click behavior under headers.

### F11 — Excel tests run a different ClosedXML dependency

**Severity:** Medium. **Confidence:** Confirmed. **Category:** Release validation fidelity.

**Location/symbol:** [ProjectManagement.csproj](<E:/Dot Net Web Development/ProjectManagement/ProjectManagement.csproj:21>), package reference 21; [ProjectManagement.Tests.csproj](<E:/Dot Net Web Development/ProjectManagement/ProjectManagement.Tests/ProjectManagement.Tests.csproj:25>), package reference 25.

**Evidence:** application pins 0.104.0, test project directly overrides with 0.105.0. Test execution uses its graph rather than the application's standalone publish graph.

**Scenario/impact:** serialization/calculation export test can pass against newer behavior while release uses older behavior. No specific corrupted workbook is asserted.

**Minimal remediation:** align deliberately or remove the overriding reference; validate representative exports through release dependencies. **Regression:** graph/version fidelity plus opening release-generated workbooks/formulas. **Coverage:** many workbook tests exist; differing dependency selection undermines their release fidelity.

### F12 — Search PostgreSQL Facts silently pass without integration execution

**Severity:** Medium. **Confidence:** Confirmed. **Category:** CI false-green coverage.

**Location/symbol:** [SearchV2PostgresIntegrationTests.cs](<E:/Dot Net Web Development/ProjectManagement/ProjectManagement.Tests/Search/SearchV2PostgresIntegrationTests.cs:13>), connection and eleven Facts, 13–274; both normal/migration workflow definitions.

**Evidence:** every Fact returns successfully when `PRISM_SEARCHV2_TEST_CONNECTION` is absent. Neither workflow supplies it. Migration job filters other classes; normal suite can count these no-ops as passed.

**Scenario/impact:** PostgreSQL search semantics regress despite apparently passing integration tests. InMemory/source-string tests cannot validate those SQL primitives.

**Minimal remediation:** explicit skips locally; required initialized PostgreSQL search job fails missing configuration. **Regression:** required job configuration fail-fast and evidence each Fact executes on PostgreSQL. **Coverage:** tests are present, execution is not established by current CI; migration test's explicit guarded skip is a useful contrast.

### F13 — Manual replacement-ZIP workflow invokes deleted generator

**Severity:** Low. **Confidence:** Confirmed. **Category:** CI/operator packaging.

**Location/symbol:** [build-project-tot-v3-zip.yml](<E:/Dot Net Web Development/ProjectManagement/.github/workflows/build-project-tot-v3-zip.yml:26>), Generate replacement files and ZIP steps, 26–34.

**Evidence:** invokes `ReadyToReplace/Project-Tot-Precision-and-UX-v3/build_package.py`; exact-HEAD tree contains no ReadyToReplace files and Test-Path returned False. Manual trigger remains exposed after housekeeping.

**Scenario/impact:** operator dispatches the advertised job; Python cannot open generator and packaging fails. It is not the application's publish path and should not be ranked as ERP outage.

**Minimal remediation:** remove obsolete workflow or replace with a documented current packaging function; do not resurrect removed legacy material automatically. **Regression:** every dispatched generator/input path exists and a disposable packaging job works. **Coverage:** no current packaging validation found.

## 2. High-confidence risks

### R01 — Plan submission and decision state machine lacks atomic protection

**Severity:** High. **Confidence:** High confidence. **Category:** Approval concurrency/authority.

**Location/symbol:** [PlanApprovalService.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Plans/PlanApprovalService.cs:62>), `SubmitAsync` 62–86, approve read 132–137/transaction 167/activation 211–222, reject read 315–320/mutation 339–357; `PlanVersion`; `ApplicationDbContext:3112–3159`.

**Evidence:** no PlanVersion concurrency token or atomic PendingApproval claim. Only version and owner-Draft uniqueness exist, not one pending per project. Approval's transaction begins after loading pending state; rejection does not update Project.

**Scenarios/impact:** concurrent approval/rejection both read pending; approval applies dates/pointer, then stale rejection returns active version to Draft and clears metadata. Both can succeed. Two distinct drafts can also both pass Any(no pending) and submit; automatic realignment does not enforce that exclusion either. These manifestations are grouped under the unprotected plan state machine.

**Minimal remediation:** claim exact submission/revision inside both decision transactions; enforce singleton pending index with reviewed cleanup/conflict handling. **Regression:** real PostgreSQL approve/reject and two-owner submit races have one controlled winner and consistent active authority. **Coverage:** InMemory tests cover sequential pending exclusion/self-approval; stage decisions have stronger conditional claims. Reproduce/inventory before migration repair.

### R02 — Concurrent first procurement writes create duplicate singleton facts

**Severity:** High. **Confidence:** High confidence. **Category:** Commercial/database integrity.

**Location/symbol:** [ProjectFactsService.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Projects/ProjectFactsService.cs:225>), money upsert 225–247, SOW 70–91, supply order 192–211; `ApplicationDbContext.ConfigureMoneyFact:2587+`, SOW/supply mapping around 2696–2723; `Procurement/Edit.OnPostAsync:133–166`.

**Evidence:** SingleOrDefault(ProjectId) then add-if-missing; ProjectId indexes remain nonunique. Fact row-version tokens protect existing-row changes, not two inserts into an absent singleton. Read-committed transaction and already-cleared backfill flags need not update a shared locking row.

**Scenario/impact:** two initial AON/L1/etc. saves insert both rows; future singleton upserts throw, while latest-row reads can conceal competing values until editing fails.

**Minimal remediation:** confirm singleton/history intent; reconcile existing duplicates and enforce unique current-row semantics plus atomic upsert/conflict. Do not use arbitrary First as cleanup. **Regression:** concurrent first-create followed by another edit and existing duplicates on PostgreSQL. **Coverage:** sequential InMemory fact tests do not establish absent-row serialization. Inventory data first.

### R03 — Search incremental claims and writes are not fenced

**Severity:** High. **Confidence:** High confidence. **Category:** Search queue/convergence.

**Location/symbol:** [SearchIndexStore.cs](<E:/Dot Net Web Development/ProjectManagement/Services/SearchV2/Indexing/SearchIndexStore.cs:215>), enqueue 215–227, claim 240–249, completion 265–277, recovery 280–294/failure 298–309; foundation migration `search_v2_enqueue:197–214`; `SearchIndexWorker:115–117`.

**Evidence:** source trigger/explicit enqueue resets in-flight status to pending; another consumer overwrites StartedAtUtc. Work item has no attempt token; replacement, complete and fail are not conditioned on ownership/revision. Stale recovery similarly revokes without fencing the old attempt. V2 serves by current base config; full reconciliation interval is 1,440 minutes.

**Scenario/impact:** instance A holds old projection, intervening source edit lets B index newest state, then A publishes old data and acknowledges using B's timestamps. New work disappears. Obsolete title/body/deletion visibility can remain, not just a broken destination link; query predicates use indexed visibility. No private notebook ACL scenario is invented here.

**Minimal remediation:** preserve running ownership on enqueue, track monotonic requests and attempt tokens, fence publication/ack/failure and replay intervening revisions. **Regression:** controlled two-consumer enqueue and expired-lease races reject stale publication/ack. **Coverage:** SQL query/schema tests do not cover lifecycle races and F12 limits current integration execution.

### R04 — Targeted Activity upsert can unhide deleted catalogue metadata

**Severity:** Medium. **Confidence:** High confidence. **Category:** Media lifecycle projection.

**Location/symbol:** [PrismActivityMediaIngestionService.cs](<E:/Dot Net Web Development/ProjectManagement/Features/MediaLibrary/Outbox/PrismActivityMediaIngestionService.cs:80>), source read 80, catalogue lock 89–90, map 131/439–445; removal 223–224/273–274 and retirement 329–368; `MediaAssetVisibilityPolicy.Apply:30–43`.

**Evidence:** reads source before shared lock. A later delete consumer can commit hiding first; delayed upsert maps obsolete state, setting available and clearing deletion/archive.

**Scenario/impact:** overlapping consumers finish both events but gallery shows deleted Activity metadata until reconciliation (production five-minute interval). **Boundary:** original provider checks live deletion and retirement increments CacheVersion; unrestricted original/old derivative disclosure was not established.

**Minimal remediation:** load/revalidate source after obtaining catalogue coordination. **Regression:** barrier around pre-lock source read, delete first, resumed upsert stays hidden. **Coverage:** interceptor/visibility tests cover surrounding mechanisms, not consumer ordering; full catalogue reconciliation already reads under its lock.

### R05 — External scan commits pending assets before their jobs

**Severity:** Medium. **Confidence:** High confidence. **Category:** Media durable work/fault recovery.

**Location/symbol:** [NetworkMediaSourceScanner.cs](<E:/Dot Net Web Development/ProjectManagement/Features/MediaLibrary/Services/NetworkMediaSourceScanner.cs:384>), `FileSystemMediaSourceScanner.ProcessBatchAsync`, 268–279 and 384–397; `QueueJobsAsync:438–494`; `MediaAssetProcessor:67–75`.

**Evidence:** no enclosing transaction covers asset commit and separately saved AnalyseAsset job. Next unchanged scan continues early and may only queue classification, which does not rebuild derivatives.

**Scenario/impact:** shutdown/database error in the gap leaves pending status and no background derivative work for unchanged content. External scanning is disabled in stock production; risk requires enabling it. Lazy derivative generation on viewing may recover an image but is not job/status reconciliation.

**Minimal remediation:** atomic asset/job persistence or authoritative missing-job repair even for unchanged fingerprints. **Regression:** fault between commits then unchanged rescan yields active AnalyseAsset and status convergence. **Coverage:** no scanner commit-gap recovery test found.

### R06 — Plan changes commit before durable notification publication

**Severity:** Medium. **Confidence:** High confidence. **Category:** Producer outbox consistency.

**Location/symbol:** [PlanApprovalService.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Plans/PlanApprovalService.cs:86>), save/notify 86–91, approval commit/notify 222–232, rejection 357–366; `PlanNotificationService.NotifyAsync:165–190`; `NotificationPublisher.PublishAsync:85–106`.

**Evidence:** separate notification SaveChanges occurs after business state commits. Non-cancellation publishing failure is logged/swallowed; no stored event exists for retry if enqueue failed.

**Scenario/impact:** server stops or second save fails after plan submission/approval, returning committed work without a durable alert. Dispatcher retries cannot recover an event never recorded.

**Minimal remediation:** use existing QueueAsync capability to persist immutable event in the business transaction, dispatch after commit; preserve preference semantics explicitly. **Regression:** enqueue persistence failure rolls back or leaves recoverable event, never silent success with no event. **Coverage:** approval tests often use NullPlanNotificationService and producer tests mock publisher; neither validates this boundary.

### R07 — Failed production upload root silently changes storage location

**Severity:** High. **Confidence:** High confidence. **Category:** File durability/backup coverage.

**Location/symbol:** [UploadRootProvider.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Storage/UploadRootProvider.cs:137>), `EnsureRootWithFallback:137–166`, fallback 190–211, constructor 42–60; file backup scripts.

**Evidence:** any configured-root creation failure causes service-profile/local-app-data or content-root uploads fallback, even in Production. Backups copy separately supplied DataRoot, not this runtime fallback. Intended production root is F:/ProjectManagementData.

**Scenario/impact:** permissions/missing volume cause accepted uploads outside backup tree; permission repair later switches roots, hiding those files. Profile/site replacement can lose them.

**Minimal remediation:** production fail-closed configured storage validation; explicit development fallback; expose actual roots and backup coverage. **Regression:** unwritable production root cannot accept fallback uploads; development behavior remains intentional. **Coverage:** current UploadRootProviderTests isolate PM_UPLOAD_ROOT but still test fallback behavior, not production backup guarantees.

### R08 — Native Windows database errors can become success messages

**Severity:** Medium. **Confidence:** High confidence, masked by F01. **Category:** Operator false success.

**Location/symbol:** [backup-db.ps1](<E:/Dot Net Web Development/ProjectManagement/ops/backup-db.ps1:38>), native call/output 38–48; [restore-db.ps1](<E:/Dot Net Web Development/ProjectManagement/ops/restore-db.ps1:39>), sequence/output 39–61.

**Evidence:** no immediate LASTEXITCODE checks for pg_dump/dropdb/createdb/pg_restore; completion messages are unconditional. Native nonzero results do not ordinarily cause PowerShell terminating exceptions.

**Scenario/impact:** after F01 repair, disk-full/credential/SQL errors can still be announced as completed; later restore operations may execute after failure. Bash set-e already addresses a different mechanism and is not duplicated here.

**Minimal remediation:** check each exit before proceeding, fail nonzero, verify output and use restore fail-fast options. **Regression:** stub every utility failure; no later destructive step or success message. **Coverage:** no native-exit regression found; fix with F01.

### R10 — Optional SMTP operation outlives its disposed client

**Severity:** Medium. **Confidence:** High confidence. **Category:** LAN email async lifetime.

**Location/symbol:** [SmtpEmailSender.cs](<E:/Dot Net Web Development/ProjectManagement/Services/SmtpEmailSender.cs:16>), `SendEmailAsync:16–36`; `Program.cs` conditional SMTP/NoOp registration.

**Evidence:** returns SendMailAsync from a `using var client` scope without awaiting, disposing while send is pending. MailMessage is not disposed either.

**Scenario/impact:** configured LAN SMTP with delayed I/O can have its operation cancelled/fail before message delivery. Stock production does not activate SMTP; no mandatory public email dependency is claimed.

**Minimal remediation:** await inside client/message lifetime scopes. **Regression:** delayed isolated local SMTP receives complete message before disposal and surfaces failures. **Coverage:** no lifetime regression found; verify an actual local SMTP incident before attributing deployed symptoms.

### R11 — Automatic realignment ignores workflow order and plan semantics

**Severity:** High. **Confidence:** High confidence. **Category:** Planning/business-rule authority.

**Location/symbol:** [PlanRealignmentService.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Plans/PlanRealignmentService.cs:61>), `CreateRealignmentDraftAsync`, shell 61–66/order 75–105; `StageCodes.All:23–43`; `ProcurementWorkflow.V2Stages`; `PlanVersion:53–61`; `ProjectStageWorkflowPolicy:95–104`.

**Evidence:** clone sorts by fixed V1 FS→IPA→SOW, although default V2 is FS→SOW→IPA. It also omits PncApplicable, anchor, SkipWeekends and TransitionRule, inheriting true-PNC/NextWorkingDay defaults. Registered at Program:713; late completion invokes it through StageDecision:400–414 and StageDirectApply:551–565. Approval copies generated dates without revalidating stage plans.

**Scenarios/impact:** late V2 SOW does not shift its IPA successor; late IPA shifts SOW predecessor. A timing-only realignment of false-PNC/same-day/custom-anchor plan changes applicability/settings when approved; workflow resolver consumes the new active plan's PNC value. Grouped as unsafe semantic cloning, not separate cascading symptoms.

**Minimal remediation:** resolve canonical project's ordering/dependencies and explicitly preserve semantic settings, changing only intended downstream dates. **Regression:** V1/V2 SOW/IPA realignments plus false-PNC/same-day/custom-anchor generation and activation. **Coverage:** workflow-order/PNC calculator tests exist; direct-apply rollback uses a throwing stub. No real realignment semantic-preservation test found.

### R12 — Lifecycle change survives failure of its mandatory audit

**Severity:** High. **Confidence:** High confidence. **Category:** Transaction/audit integrity.

**Location/symbol:** [ProjectLifecycleService.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Projects/ProjectLifecycleService.cs:70>), `UpdateCompletionAsync:70–80`, cancellation 304–311, `ReactivateAsync:354–361`; [AuditService.cs](<E:/Dot Net Web Development/ProjectManagement/Services/AuditService.cs:91>), `LogAsync` separate save/rethrow; Overview lifecycle handlers.

**Evidence:** authoritative project saves precede separate AuditLog persistence; service and live Overview callers supply no encompassing transaction. Audit detaches failed log and rethrows, but cannot undo the earlier commit.

**Scenario/impact:** lifecycle save succeeds and audit insert fails; request reports error while project remains completed/cancelled/reactivated without required audit. User retries and audit attribution/recovery becomes ambiguous.

**Minimal remediation:** atomically persist state and required audit in a relational transaction; keep optional derived usage summary outside required atomicity. **Regression:** audit insert fault leaves prior lifecycle intact and no partial action. **Coverage:** ProjectLifecycleServiceTests collect audit events with a test service, not persistence failures.

### R13 — Failed notification materialization can permanently consume dispatch

**Severity:** High. **Confidence:** High confidence. **Category:** Durable notification/failure handling.

**Location/symbol:** [NotificationDispatcher.cs](<E:/Dot Net Web Development/ProjectManagement/Services/Notifications/NotificationDispatcher.cs:156>), `ProcessBatchAsync:156–199`, `RegisterFailureAsync:325–357`, `CompleteDispatch:360–365`, claim filters 246/263/282.

**Evidence:** completion sets tracked DispatchedUtc before SaveChanges inserts notification. A nonduplicate DbUpdateException detaches the notification then reaches RegisterFailure. That method saves error/retry metadata but never resets the already-set DispatchedUtc. Claims exclude completed rows.

**Scenario/impact:** one-shot PostgreSQL insert failure rolls back materialization, then database recovers for duplicate check/failure save. Dispatch is stored completed with no Notification and will never retry despite retry metadata. This is separate from producer enqueue gaps and wrong event fingerprints.

**Minimal remediation:** restore/reload pending state before persisting failure, preferably fresh ownership-conditional update; never persist completion without notification or intentional suppression. **Regression:** fail first materialization save, allow subsequent calls; dispatch stays pending and retry creates exactly one notification. **Coverage:** NotificationDispatcherTests:30 covers happy realtime delivery; unique constraints prevent duplicates, not this lost event.

### R14 — Full search generation activation can resurrect superseded source snapshot

**Severity:** High. **Confidence:** High confidence. **Category:** Search rebuild/incremental coordination.

**Location/symbol:** [SearchIndexWorker.cs](<E:/Dot Net Web Development/ProjectManagement/Services/SearchV2/Indexing/SearchIndexWorker.cs:150>), `FullRebuildAsync:150–151`; [SearchIndexStore.cs](<E:/Dot Net Web Development/ProjectManagement/Services/SearchV2/Indexing/SearchIndexStore.cs:109>), `ReplaceFullGenerationAsync:109–144`, incremental replacement 169–203, `NextGenerationAsync:401–405`.

**Evidence:** BuildAll reads source before activation transaction; no revision watermark/replay/coordination with other instances. Another worker publishes/completes incremental work in old active generation, then rebuild activates earlier snapshot and deletes other generations. Generation allocation is MAX+1 without rebuild coordination.

**Scenario/impact:** pause instance A after reading a document, delete it, let B remove it and acknowledge work, then A activates its old snapshot. Deleted description/title becomes searchable with no remaining event. Overlapping rebuilds can allocate same generation and mix snapshots. Multiple consumers/overlapping workers are prerequisites; single-loop handling of pending later events is not claimed broken by this sequence.

**Minimal remediation:** exclusive rebuild ownership, unique generation allocation, source watermark and replay/coordination before activation. Serializing only final insert does not repair stale source snapshots. **Regression:** paused full snapshot plus completed delete/update and two simultaneous rebuilds never restore obsolete entries. **Coverage:** active-generation SQL smoke test establishes queryability, not rebuild/queue ordering. Distinct from R03's attempt-ownership protocol, though repair together as search consistency work is reasonable.

## 3. Items requiring verification

### V01 — Affected ImageSharp pin; application decoder reachability remains qualified

**Severity:** High if triggered. **Confidence:** Needs verification of application exploitation; affected version confirmed. **Category:** Dependency/media security.

**Location/symbol:** [ProjectManagement.csproj](<E:/Dot Net Web Development/ProjectManagement/ProjectManagement.csproj:39>), ImageSharp reference; [MediaClassifier.cs](<E:/Dot Net Web Development/ProjectManagement/Features/MediaLibrary/Services/MediaClassifier.cs:79>), `ClassifyAsync`/`ClassifyCoreAsync:79–98`; `MediaAssetProcessor`; `MediaMetadataReader.ReadDimensions:149–158`.

**Evidence:** freshly read pin is ImageSharp 3.1.11. Fresh upstream [strip fax TIFF advisory](https://github.com/SixLabors/ImageSharp/security/advisories/GHSA-jj3q-cwqj-842r) specifies affected `>=3.0.0,<3.2.0`, fixed 3.2.0; published August 2026. No older fixed GIF advisory is incorrectly attributed to this version.

**Scenario/impact requiring reproduction:** classifier independently reopens content then sniffs through Image.Load. A writer to enabled NAS might substitute an allowed JPEG after Skia metadata validation and before later decode, reaching vulnerable TIFF code and crashing/corrupting process memory. Straight TIFF uploads are rejected by detected-format checks; default NAS extensions exclude TIFF and Skia normally blocks it first. NAS is disabled in stock production. No executed PRISM exploit or code-execution claim.

**Minimal remediation:** compatible patched current-major dependency and offline payload rebuild; immutable validated bytes or format validation at each untrusted decode boundary. **Regression:** isolated decoder process/corpus and controlled substitution, never production payload execution. **Coverage:** valid-format/classifier tests do not cover advisory substitution. Full refreshed transitive/native/npm advisory audit was not run. Current HtmlSanitizer 9.0.892 matches patched stable version in [upstream template advisory](https://github.com/mganss/HtmlSanitizer/security/advisories/GHSA-j92c-7v7g-gj3f); no unsupported package vulnerability list is supplied.

### V02 — Deployed database credentials/privileges need verification

**Severity:** High if stock profile is used. **Confidence:** Needs verification of effective deployment. **Category:** Secrets/least privilege.

**Location/symbol:** [appsettings.Production.json](<E:/Dot Net Web Development/ProjectManagement/appsettings.Production.json:6>), DefaultConnection 6–8; `Program.cs:176–194` connection/DI; publish validation.

**Evidence:** current Production JSON still has PostgreSQL superuser/default development credential and Include Error Detail; current startup consumes effective configuration without a production safety rejection. Current production-readiness prose claims credentials are absent and detailed server errors are suppressed, which is not established by these code paths. Overrides can make deployment safe.

**Scenario/impact:** stock profile is deployed without overrides, connecting with excessive privileges/known credential; database/application compromise has broader impact. This is not a claim live server uses that profile.

**Minimal remediation:** inventory effective settings, remove deployable defaults and require provisioned secrets; dedicated governed database/migration identity without cloud secret store. **Regression:** no-secret Production startup fails clearly; approved least-privilege/migration setup works. **Coverage:** no stock-profile rejection test found. Do not change migration policy without preserving operator requirements.

### V03 — Trust-all forwarded headers depend on ingress isolation

**Severity:** Medium. **Confidence:** Needs verification of topology. **Category:** Proxy/audit integrity.

**Location/symbol:** [Program.cs](<E:/Dot Net Web Development/ProjectManagement/Program.cs:415>), forwarding 415–420/middleware 1212; LoginModel RemoteIpAddress recording near 74.

**Evidence:** KnownNetworks/KnownProxies are cleared; X-Forwarded-For/Proto accepted without configured sender restriction. Authentication records resulting address.

**Scenario/impact:** directly reachable Kestrel or proxy retaining attacker headers allows IP/scheme spoofing, corrupting audit and future IP throttle assumptions. Correctly isolated IIS can constrain it; not a universal deployed bypass.

**Minimal remediation:** actual trusted peers, authoritative edge headers, closed backend access. **Regression:** trusted forwarding works and untrusted cannot change recorded address/scheme. **Coverage:** no topology trust test found; inspect bindings/firewall before remediation.

### V04 — Operator deployment contract disagrees with code

**Severity:** Medium. **Confidence:** Needs verification of active procedure/environment. **Category:** Offline deployment/startup.

**Location/symbol:** [offline-ws2022.md](<E:/Dot Net Web Development/ProjectManagement/docs/deployment/offline-ws2022.md:44>), no-auto-migration guidance 44–48, HTTP setup 142–155, health checks 156/168; [production-readiness.md](<E:/Dot Net Web Development/ProjectManagement/docs/deployment/production-readiness.md>), startup/settings claims; production-checklist readiness; Program startup/key/cookie composition.

**Evidence:** old offline guide still prescribes HTTP and no auto migrations; executable mandates migrations and secure host cookies. New guide says failures of either context are fatal, whereas Program catches media startup failure and continues core ERP. New guide also claims absolute/write-tested keys and Windows DPAPI; current configuration creates XmlRepository/Directory without those explicit checks/registration. `/health` and `/health/ready` instructions lack a mapped route in current source searches; authenticated Admin diagnostics exist. Default Production key path remains `/var/pm/keys`. Bash publish remains framework-dependent/no apphost versus Windows's self-contained payload.

**Scenario/impact:** operators follow conflicting guidance, get HTTP cookie/antiforgery failure, unexpected DDL, unprepared Windows key ACL/path, false readiness assumptions or wrong payload. New configuration override fixes test keys, not these deployment assertions. Runtime key encryption/actual ingress must be verified, not inferred absent solely from one method name.

**Minimal remediation:** one truthful executable/operator contract, LAN TLS, durable explicitly provisioned keys/storage, current migration/media degradation policy and real readiness check. Preserve existing keys/application name; do not weaken cookies for HTTP directions. **Regression:** clean least-privilege offline IIS package/start/restart and end-to-end readiness, OCR/model/fonts. **Coverage:** new host-key/OCR isolation tests and Compendium self-test are useful components, not full deployment proof.

### V05 — Procurement assignment scope is ambiguous

**Severity:** Medium if assignment restriction is intended. **Confidence:** Needs verification of business policy. **Category:** Resource authorization.

**Location/symbol:** [Procurement/Edit.cshtml.cs](<E:/Dot Net Web Development/ProjectManagement/Pages/Projects/Procurement/Edit.cshtml.cs:53>), `OnPostAsync`, role/id/completed-stage checks; `Overview.cshtml.cs:584–605` procurement edit flags.

**Evidence:** Project Officers can post other completed-project IDs without assignment guard, but current UI also grants role-wide completed-stage edit flags. No current authoritative assignment restriction specific to procurement was established.

**Scenario/impact to verify:** unassigned PO alters another project's commercial fact if product intent requires assigned ownership. If role-wide authority is intentional, this is not IDOR and needs no restriction fix.

**Minimal remediation:** clarify/document policy first; if assignment-scoped, enforce actual-resource guard server-side. **Regression:** assigned/unassigned roles according to approved matrix. **Coverage:** actual-date pages test assigned scope elsewhere, which cannot establish procurement policy. Do not implement a guessed permission reduction.

## 4. Code-quality observations that are not bugs

1. Current root README/AGENTS/document index are improved and durable migration/operator assets have moved to organized paths. Housekeeping is not itself a correctness defect; F13 is the concrete leftover callable workflow.
2. V1/V2 search coexistence and shadow rollout are not defects merely because both are registered. Resolve R03/R14 before consolidation; measure semantic/access equivalence rather than removing fallback by naming preference.
3. Numerous JS/.NET source-contract tests are useful structural guards but do not establish PostgreSQL concurrency, CSP or async I/O behavior. The 812 passing JS tests are bounded evidence, not proof all domains work.
4. No measured N+1/Include explosion/client-evaluation performance incident was established. Obtain representative database plans/query counts before targeted optimization.
5. Sampled formal read/export paths consume canonical authority resolvers. No blanket dashboard/Excel/PDF/PPT/JavaScript discrepancy claim is made. Cross-output authority comparisons remain valuable targeted tests; R11 is a concrete alternate business-rule implementation.
6. Runner/SDK patch choices, action pinning and package refresh cadence merit maintenance. No action age alone is called a vulnerability. Required restore/network acquisition is a build concern distinct from deployed Internet requirements.

## Repair order, PR boundaries and regression priorities

### Recommended repair order

1. Recovery prerequisites: F01 with R08; F02 in a separate nondestructive restore change. Verify isolated recoverable backup before schema/data repair.
2. File durability R07 and verified stock configuration/runbook prerequisites V02/V04.
3. Plan authority F06/R01 and realignment R11; commercial singleton R02 separately; chronology F07; required lifecycle audit R12.
4. Security enforcement F03/F05 and login partition F04; verify V03 before relying on client IP.
5. Notification R13, then F08/R06 with immutable event identity. These are three distinct causes, requiring distinct fault/lifecycle tests.
6. Search R03/R14 and media R04/R05 with controlled real-provider coordination tests.
7. Minimal affected image dependency patch V01 and disconnected payload validation; do not wait for classifier redesign.
8. Validation fidelity F09/F11/F12, orphaned workflow F13 and small F10/R10. CI improvements may be undertaken earlier to support all preceding repairs.

### Suggested PR boundaries

| Boundary | Findings | Required validation |
| --- | --- | --- |
| Windows backup entry/exit | F01 + R08 | harmless utility stubs and each failure path |
| Nondestructive restore | F02 | disposable corrupt/partial restore drills preserve original |
| Immutable/atomic plan state | F06 + R01 | old forms/resubmissions and competing PostgreSQL decisions |
| Realignment semantic clone | R11 | workflow-specific downstream stages and all preserved settings |
| Procurement uniqueness/current fact | R02 | duplicate inventory, cleanup migration and absent-row race |
| Chronology closure | F07 | predecessor-only/coherent batch correction |
| Lifecycle audit atomicity | R12 | actual audit-insert fault rollback |
| Account/password restriction | F03 | real Identity across endpoint families |
| Minimal API request integrity | F05 | cookie/token HTTP matrix |
| Login attempt partitioning | F04 after V03 | clients/GET/POST and trusted forwarding |
| Notification failed materialization | R13 | failed first insert then recovered retry |
| Plan event identity/outbox | F08 + R06 | resubmission versus retry and transactional producer fault |
| Search consistency | R03 + R14 | attempt fencing, snapshot watermark/replay and simultaneous rebuild |
| Targeted Activity catalogue ordering | R04 | source-read/delete/lock barriers |
| External scan asset/job consistency | R05 | split-save failure and unchanged rescan |
| Production storage | R07 | root failure, intentional fallback and backup coverage |
| CI/test fidelity | F09/F12, F11 separately | guard fixtures/required SQL execution; release workbook dependencies |
| Minor UI/email/operator cleanup | F10, R10, F13 as separate small PRs | CSP browser; delayed LAN SMTP; valid packaging inputs |
| Decoder security update | V01 | patched-version compatibility, isolated corpus, offline publish |
| Deployment/policy alignment | V02–V05 after verification | effective config/IIS/readiness and approved authorization matrix |

### Fix before architectural refactoring

Protect recovery, authoritative approvals/facts/chronology, required audit/outbox, request/account boundaries and durable storage first. Correct queue/publication semantics before search consolidation or worker restructuring. No microservices, SPA rewrite, generic EF repository or cloud queue/storage is necessary.

### Do not combine

- Procurement duplicate-data cleanup with unrelated media/search migrations.
- Password/CSRF/login repairs with broad UI/navigation redesign.
- Image security dependency update with classifier/face-model redesign.
- Recovery safety with live production data cleanup or destructive restore.
- Key relocation with application-name/cookie-name changes; key continuity requires explicit separate validation.
- Performance cleanup/V1 deletion with search ownership/generation correctness work.
- A guessed procurement permission reduction with otherwise mechanical concurrency fixes.

### Reproduction and inventory required before final fixes

Use PostgreSQL barriers/fault injection for R01/R02/R03/R04/R05/R12/R13/R14; inventory existing duplicate/pending data for constraints. Exercise R07 root failures, R10 delayed local SMTP and R11 realignment generation/activation. V01 needs isolated decoder/substitution checks and fresh full dependency audit; V02 effective secrets/privileges; V03 actual ingress; V04 clean offline IIS SOP; V05 approved permissions. Never run hostile decoder or destructive restore reproduction in production. Safe F01/F09 checks have already reproduced their local causes, but repaired behavior still needs regressions.

## Missing regression categories

- Real-provider simultaneous decisions/submissions, absent-row uniqueness and stale reviewed revision.
- V1/V2 realignment order and preservation of optional-stage/anchor/calendar semantics.
- Successor closure on predecessor correction; lifecycle mutation plus audit failure atomicity.
- Flagged real-cookie accounts across pages/controllers/minimal APIs/hubs; unsafe minimal actions with missing/wrong tokens; per-client login limits.
- Immutable event identity, producer outbox atomicity and dispatcher failed-insert recovery.
- Queue claim fencing/lease expiry and full-generation snapshot/replay ordering.
- Targeted source deletion/catalogue race and scanner split-commit recovery.
- Production Windows storage/key/ACL/backup coverage; nondestructive recovery and native-process status handling.
- Real shipped CSP control behavior, delayed SMTP, release dependency/workbook fidelity and isolated decoder corpus.
- Required initialized SQL integration execution and cross-output canonical lifecycle/commercial/stage facts. These are categories insufficiently established by the inspected tests, not a claim no related test exists anywhere.

## CI/CD improvements worth separate work

1. Fix guard error semantics/current markup policy; preserve already-correct master triggers. Remove the orphaned ZIP job or restore a truthful current purpose.
2. Required initialized PostgreSQL search suite with explicit skips outside it, mandatory configuration inside it, and targeted concurrency/fault tests. Retain strong migration metadata/drift gates.
3. Windows Release validation for deployed target, PowerShell tools and host/storage/key isolation. Do not regress the current endpoint OCR/Search isolation fix.
4. Self-contained win-x64 disconnected package smoke covering application start, login, storage, OCR executables/languages, models, fonts/native libraries/templates and representative exports. Compendium-only self-test is a component of that gate.
5. Align production/test dependencies; refreshed direct/transitive/native advisory/SBOM checks on build infrastructure with defined severity gate and offline patch process. Runtime must not fetch feeds/packages.
6. Versioned source SHA/checksums, intentional SDK/runtime/tool versions, config/migration impacts and rollback notes; reconcile actual mandatory startup/media-degradation policy with operators.
7. Require actual readiness evidence rather than nonexistent health URLs or old validation transcripts. The current local JS pass and inspected .NET regressions must not be reported as a full production certification.

**End of review.** Exact requested master was reviewed. No application fixes, commits, issues or pull requests were made; only this report was replaced after the authorized checkout synchronization.
