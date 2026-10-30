# PRISM ERP: Admin module and Admin UI/UX review (October 2026)

**Status:** this is a review of the code, not an operator runbook. No fixes are included. Each finding must be re-checked against the code and the deployed configuration before it is fixed. This review did not inspect production.

- **Reviewed commit:** `ae32458` (branch `claude/jolly-ride-vjhjxw`, the same as master at review time).
- **Date:** 30 October 2026.
- **Scope:**
  - `Areas/Admin/**` (50 PageModels plus the shared partials)
  - `Pages/Admin/**` (Media Sources and Media Intelligence)
  - `Services/Admin/**`
  - the Holidays and Celebrations pages that the admin catalog links to
  - the admin minimal-API endpoints in `Program.cs`
  - `wwwroot/css/admin.css` and `wwwroot/js/admin/*`
  - the related tests

## How the review was done

1. **Code review.** Five reviews ran in parallel, each reading its slice end to end:
   - identity, access and audit
   - master data
   - operations, recovery, diagnostics and media
   - UI/UX for the operational pages
   - UI/UX for the master-data and media pages, plus `admin.css`

   The coordinator then re-read the code behind the highest-severity findings. Those findings are marked **Verified** below.
2. **Running app.** The application was built (`dotnet build`, 0 errors) and run locally:
   - Development environment
   - a fresh PostgreSQL 16 database with migrations applied
   - the bootstrap administrator
   - OCR, Search V2 and the Media Library disabled

   Playwright then captured all 36 admin pages:
   - light theme at 1440 px
   - light theme at 390 px
   - dark theme at 1440 px

   Every page returned HTTP 200 with no console errors. Horizontal overflow was measured on each page. The screenshots are not committed.
3. **Not executed:** `dotnet test`, the PostgreSQL migration tests, and any destructive or concurrency reproduction.

## Executive summary

- **Authorization coverage is complete.** All 53 admin PageModels carry their own `[Authorize(Policy|Roles)]`. Note that `AuthorizeAreaFolder("Admin", "/")` only requires the user to be authenticated, and `Pages/Admin` is outside that convention altogether. No GET handler mutates state, and no page opts out of antiforgery. HoD cannot assign roles.
- **No Critical issue was found.**
- **Data-integrity defects (High):**
  - the calendar recovery page is non-functional
  - the document bulk purge can permanently delete live documents
  - a project purge can race a restore and delete the restored project
- **Security/operations defects (High):**
  - disabling a user or removing a role takes up to 30 minutes to take effect, yet the UI says it took effect immediately
  - a database error in `UserPurgeWorker` stops the whole host
- **UI rendering defects (High):**
  - about two dozen admin layout classes have no CSS, so every master-data Create/Edit/Delete/Deactivate page renders without its intended layout
  - the Search Index and Chat Storage metric tiles render as run-together text
  - the document purge modal cannot submit its confirmation
  - flash "Dismiss" buttons do nothing outside the Users pages
  - admin text sits far below the house type and contrast floors

## Priority order for remediation

| # | Finding | Severity | Status |
|---|---|---|---|
| 1 | B-H1 Calendar recovery never finds deleted events | High | Verified |
| 2 | B-H2 Document bulk purge acts on non-recycled ids; `HardDeleteAsync` has no state check | High | Verified |
| 3 | B-H3 Project purge does not re-check state or retention inside its transaction | High | Code read |
| 4 | A-H1 Session revocation lag (no `SecurityStampValidatorOptions`) | High | Verified |
| 5 | A-H2 `UserPurgeWorker` stops the host on a database error | High | Verified |
| 6 | U-H1 Unstyled form and operation classes on master-data pages | High | Verified (screenshot) |
| 7 | U-H2 Document purge modal inputs sit outside the form | High | Verified |
| 8 | U-H3 Flash dismiss is wired only on Users pages | High | Code read |
| 9 | U-H4 Type scale and muted contrast below the house floors | High | Code read |
| 10 | M-H1 Category delete misses FK references and returns HTTP 500 | High | Code read |

Fix 2 and 7 together. The modal defect currently makes the unsafe delete path unreachable from the UI. Fixing only the modal would expose the purge of live documents.

---

## A. Identity, access and audit

### A-H1: disable, role removal and password reset do not end existing sessions promptly (High, Verified)

- **Where:** `Program.cs`. Nothing in the repository configures `SecurityStampValidatorOptions`, so the default 30-minute `ValidationInterval` applies.
- **What already works:** `UserLifecycleService` and `UserManagementService` rotate the security stamp correctly.
- **The gap:**
  - Role claims stay cached in the cookie until the stamp is next re-validated.
  - `EnforcePasswordChangeFilter` checks only `MustChangePassword`.
  - Meanwhile `Users/Disable` and `Users/Reset` flash "Existing sessions were invalidated."
- **Scenario:** an Admin disables a compromised account, or removes `Admin` from a user. That user keeps access, including admin pages and SignalR, for up to 30 minutes.
- **Fix (additive):**
  - Set `ValidationInterval` to about one minute.
  - Optionally fold a per-request `IsDisabled`/`PendingDeletion` check into the password-change filter's existing user lookup.
  - Correct the flash text.

### A-H2: `UserPurgeWorker` can stop the whole host (High, Verified)

- **Where:** `Services/UserPurgeWorker.cs:25-52`. The loop catches only `TaskCanceledException`.
- **Why it matters:** with .NET 8's default `BackgroundServiceExceptionBehavior.StopHost`, an `NpgsqlException` during a database restart or failover takes all of PRISM down. `SearchIndexWorker.cs:8` already documents exactly this hazard.
- **Fix:**
  - Catch and log per tick with backoff.
  - Use `IClock` instead of `DateTime.UtcNow`.

### A-M1: a failed purge leaves the account permanently stuck (Medium)

- **Where:** `UserLifecycleService.cs:243-321`.
- **Scenario:** the user owns rows with `DeleteBehavior.Restrict` FKs, so the purge fails. The worker retries every minute forever. The undo window has expired. Edit, Enable and Reset are all blocked by `PendingDeletion`.
- **Fix:**
  - Check for dependent records in `RequestHardDeleteAsync` and offer "Disable instead".
  - On purge failure, revert to the disabled state and audit it.

### A-M2: concurrent user edits silently overwrite role changes (Medium)

- **Where:** `Users/Edit` posts no `ConcurrencyStamp`. `UpdateUserAsync` diffs the posted roles against the current roles.
- **Scenario:** a stale form can undo another admin's role revocation without any warning.
- **Fix:** round-trip `ConcurrencyStamp` and reject the save on mismatch.

### A-M3: a forced password change accepts the temporary password again (Medium)

- **Where:** `Areas/Identity/Pages/Account/Manage/ChangePassword.cshtml.cs:70-84`.
- **The gap:**
  - Nothing checks that the new password differs from the old one, so the admin-issued password can survive the forced change.
  - The password policy is weak: 8 characters, with only a lowercase letter required.
  - The change itself is not audited.

### A-M4: `EnforcePasswordChangeFilter` covers Razor Pages only (Medium)

- **Where:** it is an `IAsyncPageFilter`.
- **The gap:** minimal APIs, `Controllers/Api/*` and SignalR hubs remain usable with a temporary credential.
- **Fix:** move the check to middleware, or use a claim plus a policy requirement.

### A-M5: bulk exports of user and security data are not audited (Medium)

- **Where:**
  - `Users/Index` `OnGetExport*` (up to 100k rows)
  - `AccessGovernance/Index`
  - `Logs/Index` (50k rows, including raw before/after JSON)
  - `Analytics/Logins`
- **Fix:** record an export audit entry with the filters and row count.

### A-M6: the audit-log page scans the whole table on every view (Medium)

- **Where:** `AdminLogQueryService.GetAsync:94-147`. With no default date range it runs, over the whole table:
  - `COUNT(*)`
  - `DISTINCT Action`
  - a per-day `GroupBy` over the entire history
  - a distinct-actor count
  - `DataJson LIKE '%…%'`
- **Fix:**
  - Default to the last 30 days.
  - Limit the chart to that range.
  - Cache the action options.

### A: Low and Info

- **Self-demotion.** An Admin can remove their own Admin role through Edit. Disable and Delete block self-targeting; Edit does not. (`Users/Edit.cshtml.cs:102-170`)
- **"Active administrator" defined three ways.** It differs between `UserManagementService`, `UserLifecycleService` and `AdminAccessGovernanceService` (locked accounts count in two of them). `AccessGovernance/Index.cshtml.cs:84` also re-derives account state in the PageModel.
- **Capability drift.**
  - `Pages/Admin/MediaIntelligence/*` use `[Authorize(Roles="Admin,HoD")]` rather than a catalog policy.
  - `AdminPolicies.MediaClassificationManage` is registered but unused.
  - `AdminRoleAccessCatalog` hard-codes roles instead of reading `AdminCapabilityCatalog`.
  - No test asserts that every admin PageModel has a policy that exists in the catalog.
- **Dead code.** `UserManagementService.DeleteUserAsync` is an unused immediate hard-delete that bypasses the controlled deletion workflow. `GetUsersAsync`, `UpdateUserDetailsAsync` and `UpdateUserRolesAsync` are unused too.
- **Failed-login records.**
  - They store the typed username verbatim, so a password typed into the username field is retained and exported.
  - Per-user correlation compares `UserName` case-sensitively.
- **Search input.** `%`/`_` are not escaped in admin search filters, and the search terms have no length limit.
- **Password reset.** It does not clear `LockoutEnd`/`AccessFailedCount`.
- **Needs verification: possible slow regex.** `AdminClientDescriptorService.SafariRegex` has no timeout and runs on stored, untruncated User-Agent strings. Benchmark it, and truncate User-Agents when they are stored.
- **Needs verification: last-admin guard on PostgreSQL.** The guard relies on Serializable isolation. It is tested only on the InMemory provider.

### A: Strengths

- Last-admin and self-target guards live in the services, not just the UI.
- Audit rows commit atomically with each lifecycle change and record before/after state.
- Typed-username confirmation is used for destructive actions.
- Pages send no-store caching headers.
- `SafeCsv` neutralises formulas.
- `LocalRedirect` is used on login.
- Lists are paged.

---

## B. Operations, recovery, diagnostics, media

### B-H1: the Deleted Calendar Events page can never show or restore anything (High, Verified)

- **Where:**
  - `Data/ApplicationDbContext.cs:2996` declares `HasQueryFilter(x => !x.IsDeleted)` on `Event`.
  - `Services/Admin/Calendar/CalendarRecoveryService.cs:90,189,202` query `_db.Events…Where(e => e.IsDeleted)` without `IgnoreQueryFilters()`.
  - `AdminRecoverySummaryService` and `AdminDashboardService` count deleted events the same way.
- **Result:** the list is always empty, every restore returns "could not be found", and the deleted-event counts are always 0.
- **Contrast:** the project queries in the same service do call `IgnoreQueryFilters()`.
- **Fix:** add `IgnoreQueryFilters()` and a test against the real model's query filter.

### B-H2: the document recycle bulk action also hard-deletes live documents (High, Verified)

- **Where:**
  - `Areas/Admin/Pages/Documents/Recycle.cshtml.cs:81-114` checks only `selection.EligibleCount == 0`, then loops over **all** posted ids.
  - `DocumentService.HardDeleteAsync` (`:497`) never checks `Status == SoftDeleted`.
  - `RestoreAsync` is equally unconditional.
- **Scenario:** admin B restores document X. Admin A, on a stale page, then purges a selection containing X. A published document and its file are destroyed. A crafted `SelectedIds` value achieves the same without any race.
- **Current exposure:** this path is unreachable from the UI only because of U-H2.
- **Fix:**
  - Iterate only the eligible ids.
  - Re-check the state inside `HardDeleteAsync` under a row lock or a conditional delete.

### B-H3: a project purge can delete a project that was just restored (High)

- **Where:** `ProjectModerationService.PurgeAsync` (`:198-217`) checks `IsDeleted` on an untracked read before the transaction. `PurgeProjectDatabaseRecordsAsync` (`:522`) reloads and removes the project without re-checking.
- **Scenario:** the retention worker, or an admin purge, races a restore. The restore commits, then the purge deletes the restored project.
- **Retention gaps:**
  - The retention window is enforced only in `Trash.cshtml.cs:96`, and only when `PurgeScheduledUtc` has a value.
  - The `/api/projects/{id}/purge` endpoint (`Program.cs:1936-1950`) has no retention check, no confirmation, no reason and no purge-authorised audit.
- **Fix:**
  - Move the state and retention checks into the service, inside the transaction (`FOR UPDATE`, or a conditional delete where 0 rows means abort).
  - Treat a null `DeletedAt` as not due.

### B: Medium

- **M1: unused purge/restore API.**
  - `POST /api/projects/{id}/restore-trash` and `/purge` have only one caller, the unreferenced `wwwroot/js/admin/project-trash.js`.
  - They have no antiforgery validation, though the SameSite=Strict cookie and the JSON preflight reduce the risk.
  - Remove the endpoints and the dead script together once logs confirm no operator uses them.
- **M2: PDF ingestion path safety** (`PdfIngestionCoordinator.ResolveAbsolutePath`).
  - When resolution fails, it falls back to the raw stored key.
  - `UploadPathResolver.ToAbsolute` accepts rooted keys with no containment check.
  - Its prefix test lacks a trailing separator.
  - A corrupt or legacy key could therefore copy an arbitrary server file into the repository.
- **M3: PDF ingestion runs inside the HTTP request.**
  - It buffers every PDF in memory.
  - It is guarded only by an in-process gate.
  - `DocRepoExternalLinks (SourceModule, SourceItemId)` is not unique, so duplicate links are possible.
  - Files are written before `SaveChanges`, so a failed insert orphans the file.
- **M4: process-local status.** `AdminWorkerStatusRegistry`, `PdfIngestionRunHistory`, the ingestion lock and the health cache are all process-local. With overlapping processes the System Health page shows only the serving process, and `RunId` redirects can return 404.
- **M5: retention worker hides failures.** It reports "Healthy" when individual purges failed, because `PurgeExpiredAsync` returns only the success count. Every process also runs the worker.
- **M6: Media Intelligence authorization.** The pages use role-based authorization, and `MediaClassificationOverrideService` performs no policy check of its own. These pages include the biometric approval.
- **M7: "Reclassify stale" is unbounded.** It loads all stale assets and rewrites them in a single transaction inside the request, from the PageModel (`Classifications.cshtml.cs:303-420`).
- **M8: path probe needs only `MediaView`.** `MediaSourceAdminService.TestAsync` probes arbitrary local or UNC paths. A UNC path can trigger outbound SMB authentication. Require `MediaConfigure`.

### B: Low

- **Dead trash script.** `project-trash.js` is a dead duplicate of `admin-project-trash.js`.
- **Legacy import.** A successful import is reported as failed if the audit write afterwards throws. Staging lives under node-local `App_Data`.
- **Trash purge exceptions.**
  - The page does not catch purge exceptions, so the user gets a 500.
  - The audit trail then holds "authorised" with no outcome.
- **`RemoveAssets=false`.** It leaves `uploads/projects/{id}` behind permanently.
- **Search Index diagnostics.** They render raw `ex.Message`, against the registry's own rule.
- **System Health.** It reports the migration setting from `Database:ApplyMigrationsOnStartup`, but `Program.cs` hard-codes migrations on.
- **Ingestion CSV.** It does not neutralise a leading tab or CR, and it duplicates `SafeCsvWriter`.
- **Recent recovery operations.** The query uses a leading-wildcard `Contains` on `AuditLogs`.
- **Calendar event deletion.** It records no `DeletedAt` and writes no audit entry.

### B: Strengths

- Files are quarantined, then the database transaction runs, then the files are deleted; quarantined files are restored on rollback.
- Project purge requires the typed name, an acknowledgement and a reason.
- Chat storage clean-up is transactional, audited and can be undone for a day.
- Calendar restore uses a conditional `ExecuteUpdate`, which is correct once B-H1 is fixed.
- The health page shows only the host and database name, never the connection string.
- No external URLs were found.

---

## C. Master data

### M-H1: category delete misses FK references and throws HTTP 500 (High)

- **Where:** `AdminMasterDataCommandService.cs:494-552` checks only child categories and `Projects`. These Restrict FKs are missed:
  - `ProjectLegacyImport.ProjectCategoryId` and `.TechnicalCategoryId` (`ApplicationDbContext.cs:1029-1036`)
  - `ProjectMetaChangeRequest.TechnicalCategoryId` (`:2253`)
- **Why it surfaces as a 500:** `SaveDeleteAsync` catches only concurrency exceptions, and the Delete impact page says "can delete".
- **Fix:**
  - Use one shared usage query for both the impact page and the command.
  - Map SQLSTATE 23503 to an "in use" result.

### C: Medium

- **Category activate/deactivate is a blind toggle.**
  - It runs `IsActive = !IsActive` (`:555-585`) with no target state and no RowVersion. Two stale clicks cancel each other out.
  - Deactivating a parent leaves active children beneath it, which the integrity check then reports as `active-parent-state`.
  - The flat lookups already use an explicit `SetActive(id, bool, rowVersion)`.
- **Flat lookups can never be deactivated once used.** Project types, sponsoring units and line directorates cannot be deactivated once any project references them (`:798-801`), even though categories, activity types and ARPP all allow it. Choose one rule for all of them; this changes production behaviour, so plan it as a deliberate change.
- **Audit outside the transaction.** Activity types and ARPP reference data write their audit entries after `SaveChanges`:
  - `ActivityTypeService` calls a repository that saves internally.
  - `ArppReferenceDataService:221-229,354-366` logs after saving.
  - ARPP detects unique violations with `Message.Contains("unique")` instead of SQLSTATE 23505.
- **Celebrations lack a concurrency token.** An additive, nullable RowVersion would fix this.

### C: Low

- **Re-parent race.** The cycle check runs outside the transaction, so two concurrent re-parents can create a cycle. Use an advisory lock per taxonomy.
- **Holiday conflict refresh is a no-op.** The "refresh token after conflict" code (`Holidays/Edit|Observe|WithdrawObservance`) never removes the stale ModelState value.
- **Four name normalisations.** The command, the database index, the integrity check and ARPP each normalise names differently. Use `MasterDataName` everywhere.
- **`SortOrder` is ignored.** Lookup create/edit accept `SortOrder` and then discard it.
- **Move up/down.**
  - It ranks across hidden inactive rows.
  - Category move and toggle forms drop the `q`/`status` filters.
- **Policy checks inside services.** They are missing in the integrity repair, holiday, celebration and ARPP services. `ActivityTypeService` hard-codes `IsInRole("Admin") || IsInRole("HoD")`.
- **Integrity repair audit.** It does not record previous sort orders.
- **Office-holiday predicate duplicated.** `Gazetted || IsObservedAsOfficeHoliday` appears in about six places, against the AGENTS.md rule on duplicated calculations.
- **Master Data centre.**
  - It shows Holiday and Celebration cards to users who cannot open those pages.
  - Its "recent changes" include personal names.
  - "In use" is inflated by `InUse = Total` for those domains.
- **Info:**
  - `wwwroot/js/admin/categories.js` is dead.
  - The Categories/TechnicalCategories pages and the three Lookups page sets are near-duplicates that could share a base.
  - No depth limit on categories.
  - Inactive parents are offered without any marker.
  - Holiday delete is a hard delete with no confirmation.

### C: Strengths

- Database-backed, case-insensitive unique indexes.
- RowVersion on most entities.
- The command, holiday and celebration services commit the change and its audit in one transaction.
- Hierarchy validation catches self-parenting, moving under a descendant and whole-graph cycles.
- The integrity repair is deterministic, idempotent and requires an acknowledgement.
- Holiday reclassification never silently reopens a closed office day.

---

## D. Admin UI/UX

### Observations from the running app

| Page | Observation |
|---|---|
| Master-data Create/Edit (all) | The guidance `<aside>` drops below the form with a full-size `<h2>`. The "Active" switch renders as an unstyled checkbox reading `ActiveMake this category available…`. Seen in both light and dark themes. |
| Diagnostics/SearchIndex | Metric tiles render as run-together text, e.g. `StatusAttention requiredActive generation 0`. |
| AccessGovernance | The capability matrix overflows the page: +103 px at 1440 px and +858 px at 390 px. Columns are cut off ("MAIN O…") and the whole page scrolls sideways. Six metric tiles wrap as five plus one orphan. |
| Admin overview vs AccessGovernance | On the same data, the overview says "Operational / No items require attention". AccessGovernance raises three findings, including "Only one active Administrator remains". The dashboard's attention list ignores governance findings. |
| Overview "Recent activity" | Shows "No recent administrative activity" while the Audit tile reports 3 events in 24 hours, with no explanation that sign-ins are excluded. |
| Users at 390 px | Usable. The table scrolls inside its wrapper, but nothing signals that the "Last activity" column is clipped. |

### U-H1: about two dozen layout classes have no CSS (High, Verified)

- **Classes in the markup with no rules:**
  - `admin-form-layout`, `admin-form-panel`, `admin-form-grid--two`, `admin-field--wide`, `admin-switch-row`
  - `admin-guidance-card(__icon)`, `admin-controlled-operation(--danger|--warning|__icon)`
  - `admin-operation-facts`, `admin-operation-notice`, `admin-confirmation-field`, `admin-date-fieldset`
  - `admin-panel__meta`, `admin-monitoring-metric--info`
  - `admin-monitoring-metrics--six`, `admin-table-primary`, `admin-search-control`, `admin-page-header__content`
- **Meanwhile:** `admin.css:2641-2711` styles a parallel `admin-master-form-*` / `admin-master-operation-*` family that no markup uses, and that block references an undefined `--admin-shadow-sm`.
- **Affected pages:**
  - every Create/Edit/Delete/Deactivate page for Categories, TechnicalCategories, the three Lookups and ActivityTypes
  - Holidays and Celebrations/Edit
  - the SearchIndex, Chat/Storage and IngestExternalPdfs metric tiles
- **Fix:**
  - Rename the dead CSS to the names the markup uses.
  - Convert SearchIndex and Chat/Storage to `_AdminPageHeader` and `_AdminMonitoringMetric`.
  - Add a test that fails when a `.cshtml` uses an `admin-*` class that has no rule.

### U-H2: the document purge modal cannot submit its confirmation (High, Verified)

- **Where:** `Documents/Recycle.cshtml:89-90`. The "Type DELETE" input and the acknowledgement checkbox are outside `<form id="documentRecoveryForm">` (`:37-73`) and have no `form=` attribute. Only the submit button has one.
- **Result:** the server always rejects the delete. See the note under B-H2 before fixing.

### U-H3: flash "Dismiss" does nothing outside the Users pages (High)

- **Where:** `data-admin-flash-dismiss` is wired only in `admin-users.js:25-34`.
- **Fix:** move flash dismiss, auto-submit and controlled-submit into `admin-shell.js`, which loads on every admin page.
- **Related:** `data-admin-auto-filter` on the category directory has no JavaScript behind it at all.

### U-H4: type scale and contrast below the house floors (High)

- **The floor:** 12 px for text and 11 px for labels (`docs/ux/app-shell-and-dashboard.md`).
- **Small sizes:** more than 200 `font-size` declarations in `admin.css` are under 11 px, down to 0.52 rem (≈8 px), e.g. `.admin-category-cell__copy small` and `.admin-usage-summary span`.
- **Muted colour:** `--admin-text-muted: #7b8798` is 3.65:1 on white and 3.4:1 on the admin background, against the 5:1 muted rule in `docs/ux/dark-theme-palette.md`. It is used about 90 times.
- **Fix:**
  - Map the admin type to `--pm-font-size-*`.
  - Alias `--admin-text-muted` (and the rest of the `--admin-*` light palette) to the `--pm-*` tokens.

### Cross-cutting (Medium)

- **Confirmation patterns differ across pages:**
  - **Users:** the typed username gates the button, with no explanation of why it is disabled.
  - **Project purge:** a typed name with no client-side gating. On server rejection the reason is lost.
  - **Documents:** typed `DELETE` (broken, see U-H2).
  - **Chat:** a typed word.
  - **No confirmation at all:** category, ActivityType and ARPP deactivation, and Holiday delete. Holiday delete affects schedules.
  - **Fix:** standardise on one gated pattern with an `aria-describedby` hint. Use `data-confirm` with usage counts for reversible deactivations.
- **Double-submit protection is patchy.**
  - `data-admin-controlled-submit` covers only move/toggle and integrity forms.
  - Where it is used, it disables the button even when client-side validation cancels the submit, which leaves the button stuck.
- **Shared partials are bypassed.**
  - `_ConfirmModal` and `_AdminCard` are unused.
  - Trash, Recycle, Archived, Deleted and Celebrations each hand-roll a modal; Celebrations' modal lacks `aria-labelledby` and a labelled close button.
  - Eight pages hand-roll metric tiles.
  - The flash message sits above the header on some pages and below it on others.
- **Pagination comes in four styles.** Disabled Previous/Next links stay focusable, and only Users sets `aria-current`. Add an `_AdminPagination` partial.
- **Media pages sit outside the admin shell.** MediaSources and MediaIntelligence are not in `AdminNavigationCatalog`, so they render without the sidebar, page header or flash partial. They are reachable only from the Photos menu.
- **Times are displayed in four ways:**
  - IST through `IAdminTimeService` (most pages)
  - server `ToLocalTime()` with no zone (SearchIndex, MediaIntelligence)
  - UTC (`_AdminHealthCheckRow`)
  - date-only (lookups)
- **Tables:**
  - no `scope` on `th` on the recovery, ingest, SearchIndex and Chat pages
  - empty action headers
  - Logs sort links never show the current sort and never set `aria-sort`
  - the AccessGovernance matrix uses `td` for row headers and has no sticky first column
- **Charts.** The Login activity charts have no data-table fallback, and their legend relies on colour alone.
- **Terminology:**
  - **Deleted items:** "Trash", "Recycle bin", "Deleted events", "Archived" and "Recovery centre".
  - **Deleting a user:** "Request deletion", "Delete mistaken account" and "Delete account".
  - **Login vs sign-in** are used interchangeably.
  - **Jargon:** "projection", "generation", "RRF score", "Search V2".
  - **Placeholder plurals:** "day(s)", "file(s)".
  - **Capitalised plurals mid-sentence:** "No Project types match…".
- **Colour semantics:**
  - `.admin-status-pill` defaults to green, so "Partial" and "Recurring series" show as success.
  - Holidays uses red for routine Gazetted closures; the house rule is that red means failed.

### Cross-cutting (Low / Polish)

- Decorative `<i class="bi">` icons lack `aria-hidden` on most recovery, maintenance, lookup, holiday and celebration pages.
- `aria-label` is placed on role-less `div`/`span` elements in several places, where screen readers ignore it.
- No admin page sets `ViewData["Breadcrumb"]`.
- Logs "Copy" uses only `navigator.clipboard` and fails silently on plain-HTTP LAN hosts. The Users create page already has a fallback.
- `admin.css` (3,906 lines) has grown by accretion:
  - 189 selectors are defined two or more times
  - about 26 selectors are dead
  - Phase 7 rules use px units
  - one global rule hides the "Rows" selector below 1200 px on every directory
  - the file is loaded on every page of the application, not only admin pages
- The Help page names pages but links to none of them. Its "On this page" navigation follows the content in DOM order, so on phones it sits at the bottom.

### UI/UX strengths

- Coherent shell:
  - catalog-driven sidebar with per-policy filtering
  - `aria-current` on the active item
  - persisted group state
  - a labelled mobile offcanvas
- The Users directory, User details, the Logs drawer and Chat Storage are high quality. Chat Storage's copy (preview before remove, explicit counts, the undo window explained) is the model the other recovery pages should follow.
- Dark theme coverage is nearly complete; one light-only scrollbar colour remains.
- `tools/check-views.sh` passes: there are no inline scripts or styles and no event-handler attributes.
- No CDN or remote font appears anywhere in admin; Chart.js is served locally. This complies with the offline-LAN constraint.

---

## E. Test coverage gaps

- No tests for:
  - `CalendarRecoveryService`, which would have caught B-H1
  - the Recycle page or hard delete of non-recycled documents
  - a purge racing a restore, or retention enforcement inside the service
  - `AdminMasterDataCommandService`
  - `MasterDataAdministrationQueryService`
  - `CelebrationAdministrationService`
  - `PdfIngestionCoordinator`
  - `LegacyImportPreflightService`
  - `AdminSystemHealthService`
  - `UserPurgeWorker`
  - `EnforcePasswordChangeFilter`
- No reflection test asserting that every admin PageModel has a catalog policy.
- No PostgreSQL tests for:
  - the expression unique indexes
  - FK-violation handling on category and user delete
  - Serializable last-admin guards (all lifecycle tests use InMemory)
- No test that catches `admin-*` classes used in markup with no CSS rule.

## F. Deployment notes for the fixes

All of the fixes above can be made additively:

- configuration of the security-stamp interval
- service-level re-checks
- `IgnoreQueryFilters()`
- CSS renames
- partial consolidation
- a nullable RowVersion column on Celebrations, with a backfill migration

Two fixes need a deliberate transition plan under AGENTS.md:

- changing the lookup deactivation rule (production behaviour)
- removing the `/api/projects/{id}/purge` and `/restore-trash` endpoints (route contract)

The PDF-ingestion work also needs one: making `DocRepoExternalLinks (SourceModule, SourceItemId)` unique requires de-duplicating live data first.
