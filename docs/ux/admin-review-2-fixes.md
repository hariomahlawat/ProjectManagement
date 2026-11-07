# Administration: second cold review (7 Nov 2026) and fix plan

The first review and its eleven fix PRs are in `admin-review-fixes.md`. After they shipped, two
independent reviewers looked at the module cold again:

- **UX:** every admin page in the running app, signed in as an Admin, at desktop, phone (375 px)
  and dark.
- **Code:** the Administration code against `AGENTS.md`. This time the review also included the
  Document Repository admin pages, the media pages and the social media lists.

The findings below were checked against the code.

**Verdict:**
- **Usability: 7/10** (was 6).
- **Code health: 7.5/10** (was 8 on a narrower scope; most new findings are in the Document
  Repository admin pages).
- No security hole was found.

Keep:
- the navigation: sidebar search, Every admin page and Home's job cards;
- plain empty states and IST times everywhere;
- Data checks;
- System health rows that say what to do next;
- the phone layout (no sideways scroll on any page) and dark mode.

Not product defects: the local data has missing repository files, missing photos and no
face-recognition model files, and runs in the Development environment.

## Findings

### High
1. **Restoring a repository document leaves it hidden.**
   - Approving a deletion sets `IsDeleted` and `IsActive = false`
     (`RepositoryDocumentDeleteApprovalService`).
   - Restore from the repository's deleted documents clears only `IsDeleted`
     (`Areas/DocumentRepository/Pages/Admin/Trash/Index.cshtml.cs`).
   - Readers (`DocRepoAccess.ReadableFilter`) and search only show active documents, so after
     "Document restored." nobody else can find it.
   - No test covers this.
2. **Home shows Razor code.** When nothing matches in the finder, the text reads "Try another
   word@if (Model.AllPagesHref is not null) {…". Razor reads `word@if` as an email address.
3. **Home says nothing needs you before the checks have run.** Straight after start-up the status
   reads "Not checked yet … Nothing needs you right now", while System health then shows five
   warnings.

### Medium
4. **Exports leave no audit record:**
   - Users CSV;
   - access review CSV;
   - audit log CSV;
   - sign-in CSV;
   - ERP usage CSV.

   A cut-off export is only flagged in a response header nobody sees.
5. **"Active administrator" has three definitions.** `UserManagementService` and
   `UserLifecycleService` each have their own copy of the rule. Access governance also leaves
   out locked accounts, so it can disagree with the guards.
6. **The Document Repository lists keep their logic in the PageModels.**
   - Document types and offices are near-copies of each other.
   - Their switch on/off flips `IsActive` with no row version, so a double submit undoes it.
   - The repository's deleted documents list has no paging.
   - Missing files rechecks every file on each page load, and its count can differ from System
     health's (which checks the newest 5,000).
7. **Who has which role still shows raw policy names** ("Admin.Access", "DocRepo.Purge"), and
   headings such as "Authoritative policy catalogue — Generated from registered policy metadata",
   "Restricted privileged" and "Single-holder roles".
8. **Engineering wording deeper in:**
   - System health rows: "Migration lineage … deployed assembly", "Data Protection key
     storage", "Expected cadence", "Starting; no processing cycle has completed".
   - System health's tile "23 checks completed" also counts the 4 checks that have not run.
   - Classification review: "Reclassify stale", "Face admission pending", "hybrid-media-v7".
   - The rest of Media sources.
   - Add and Edit user: "Analytics classification … command adoption reporting", "Lifecycle
     controls are separated…".
   - Archived projects, Holidays and Celebrations headings, and a configuration key in the
     guide.
9. **Figures that disagree:**
   - Media: 18 ready vs 17 prepared; 14 vs 13 unsure.
   - ERP usage: 0% of days yet 20 minutes active.
   - ERP usage: "Never recorded" for someone Users shows as active.
   - ERP usage: test accounts listed while "Include service and test accounts" is unticked.
10. **One action, several names:**
    - "Edit profile and roles" / "Edit account" / "Edit roles";
    - "Normalise order" / "Renumber 1, 2, 3…";
    - "Import projects from Excel" / "Legacy project import";
    - Chat file storage headed "Chat storage" under "Recovery".
11. **Reference lists use four layouts.**
    - The social media lists offer Delete on values in use, against the hub's rule.
    - The event types page repeats the whole platforms list.
12. **Free-text audit searches scan the whole history.** This was a deliberate choice in fix PR 4
    and will slow down as the log grows.

### Low
- Bins: the heading "Restore deleted items" sits under the tab title "Deleted projects".
- Raw user names instead of names ("Performed by abhishek", "Last run by Administrator").
- Home's "To look at" rows all open System health, not the page that fixes the problem.
- Breadcrumb section names on the media pages ("Photos administration") differ from the sidebar
  group.
- Health checks:
  - a slow network share can hold a check past its time limit, because `Directory.Exists` runs
    before the first await;
  - several admins can refresh at once.
- Legacy import's abandoned staging files are never removed.
- The social media lists use `Roles=` instead of a catalogue policy, so they are missing from the
  capability matrix.
- A placeholder comment in `OcrFailures.cshtml.cs`.
- The master-data command service keeps tracked changes after a failed save.
- Bin counts are written in three services.

## Fix plan

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **Restore really restores.** Repository restore sets the document active again, in a Document Repository service with a test; the deleted list gets paging | H1, M6 (part) |
| 3 | **Home tells the truth.** The finder's empty text is fixed, with a test that no Razor leaks; before the checks have run, Home says "open System health to run them" instead of "Nothing needs you"; "To look at" rows open the page that fixes the problem | H2, H3, Low |
| 4 | **Exports are audited.** One audit event per export with its filters and row count; a cut-off export says so in its last row | M4 |
| 5 | **One rule for an active administrator.** Shared by user management, lifecycle and access governance, with tests; bin counts from one service | M5, Low |
| 6 | **Document Repository lists through a service.** Switching on/off takes the target state and a row version; one service for types and offices; Missing files shares a capped, cached scan with System health | M6 |
| 7 | **Who has which role in plain words.** Policy names behind "Technical details", tiles and headings in everyday words; the social media lists get a catalogue policy so they appear in the matrix | M7, Low |
| 8 | **Plain words, round two.** System health rows and its tile, Classification review, Media sources, Add/Edit user, Archived projects, Holidays, Celebrations and the guide; the jargon test widened to these pages | M8, Low |
| 9 | **Figures agree.** One media count per fact; ERP usage day counting and its test-account filter reconciled with Users | M9 |
| 10 | **One name per action and one list layout.** The names in M10; the four list layouts brought to one, with Delete only on unused values; event types without the platforms list; bin and media headings match their tabs and the sidebar | M10, M11, Low |
| 11 | **Robustness.** File-system probes inside the health-check time limit and one refresh at a time; legacy staging files swept; free-text audit searches keep a default window with "All history" one click away; the change tracker cleared after a failed save; the placeholder comment removed | M12, Low |

Rules carried over:
- every PR keeps routes, policies and data contracts;
- every PR ships with tests and is checked on screen while signed in;
- every PR is merged when CI and the Codex review are clean.

PR 2 changes live data behaviour: documents restored before it stay inactive. Whether to
reactivate them in a one-off step is the owner's call. PR 2 does not do it.

**PR 2 as shipped.** `DocRepoTrashService` (Services/DocRepo) now lists the repository's deleted
documents and restores them; the Trash page only calls it. Restore clears the delete details
*and* sets `IsActive = true`, keeps the `RestoredFromTrash` audit entry in the same save, and
checks `DocRepo.Purge` itself as well as on the page. Search needs no extra call: the Search V2
trigger on `Documents` queues the reindex when the row is saved. The list is paged (10, 25, 50
or 100 rows, 25 by default) with "Showing x–y of n"; Restore and Delete for good return to the
same search, page and page size. Documents restored before PR 2 are left inactive, as decided.

**Owner decision needed:** the UX review found briefing decks deleted and restored in Recovery's
recent activity, but they are not one of the kinds listed in Restore deleted items. Whether
Recovery should list them, or say where they are restored, is left to the owner.

## PR 3 as shipped

- The finder's "Nothing matches" text is written as two plain branches, so no Razor code shows
  on screen. A test renders Home and reads that text, and a source test fails if any admin view
  writes a word straight before `@if`, `@foreach` and the like.
- Before the system checks have run, the status line says "Not checked yet. The system checks
  have not run since PRISM last started. Open System health to run them." It never says
  "Nothing needs you" until a check result exists; other things to look at are still counted.
- A "To look at" row from a system check opens the page that fixes it: Missing files, Media
  sources, Media intelligence, Chat file storage or Search index (`AdminHomeRules.HealthCheckFixPages`).
  When the viewer cannot open that page, or there is no such page, it opens System health.
- Not changed: Global Search writes `@related.Label@if (…)` straight after an expression. It is
  outside Administration, so this PR leaves it alone and the source test does not cover it.

## PR 4 as shipped

- The five personal-data CSV exports (Users, access review, audit log, sign-in activity, ERP
  usage) each write one audit event through `IAdminExportAuditService`
  (`Services/Admin/AdminExportAuditService.cs`), which goes through the existing
  `IAdminAuditService` path. The event records the signed-in user, which export, the filters that
  were set and the row count. The audit log shows it as "Users exported", "Access review
  exported", "Audit log exported", "Sign-in activity exported" or "ERP usage exported", under
  the category "Exports".
- The event is written before the file is returned. If the audit write fails, the request fails
  and no file goes out — the same as every other admin action, where an audit failure is not
  swallowed.
- When the audit log export (50,000 rows) or the sign-in export (5,000 rows) is cut off, the
  `X-PRISM-Export-Truncated` header stays and the file now ends with a plain row: "Export stopped
  at N rows; narrow the filters to get the rest." Complete exports are unchanged.
- Routes, policies and CSV columns are unchanged. Page constructors are unchanged: the service is
  taken as a handler parameter.

## PR 5 as shipped

- **One rule for an active administrator:** `Services/Admin/ActiveAdministratorRule.cs`. An account
  is active when it is not disabled and not pending deletion. The last-administrator guards in user
  management and the account lifecycle, and Who has which role, all use it. The guards still run in
  a Serializable transaction.
- **A temporarily locked administrator still counts as active.** A lock ends by itself, and an
  administrator can clear it. Counting it as inactive would make the guards stricter exactly when
  recovery matters. Before this PR, Who has which role left locked accounts out, so it could say
  "No active Administrator" while the guards still counted one. It now counts what the guards count
  and shows the lock as its own finding ("Privileged account is temporarily locked"). This also
  applies to the "Active" figure on every role card.
- **Bin counts from one place:** `Services/Admin/Recovery/AdminBins.cs` holds the queries for deleted
  projects, project documents, repository documents and calendar events (deleted projects and
  events lift the query filter, as before). Home, Restore deleted items and its tabs all count from
  it. Tests check the rule, the guards with disabled, pending-deletion and locked admins, that Who
  has which role counts the same as the guards, and that the three pages give the same bin counts.

## PR 6 as shipped

- **One service for both lists:** `IDocRepoListService` (`Services/DocRepo/DocRepoListService.cs`)
  lists, adds, renames, reorders, switches on/off and deletes repository document types and
  offices. Both pages share one thin base (`Areas/DocumentRepository/Pages/Admin/DocRepoListPageModel.cs`)
  and one table partial. Routes, handler names, form fields and the `DocRepo.ManageCategories`
  policy are unchanged; the service checks the policy too.
- **Switching takes the state wanted.** The form posts `active=true|false`, never a flip. A double
  click, or two administrators deactivating together, leaves it deactivated ("already
  deactivated"). A form without the state (a page open from before this release) is refused with
  "reload and try again" instead of being flipped.
- **Row version without a migration.** These tables had no version column. Adding one to a live
  table was judged not worth the risk, and target state alone does not protect a rename. The
  PostgreSQL model now maps the built-in `xmin` system column as the row version for the two
  tables (the same way To-do items already do): no column is added, no migration runs. Every row
  form posts its version; a change from an out-of-date page is refused with "someone else
  changed this entry". The model snapshot lists `xmin` so a later migration does not try to add it.
- **Delete only when unused.** A Delete button shows only when no document uses the value, the
  trash included. The service checks again and refuses a value in use ("Deactivate it instead").
  The documents' foreign key to these tables cascades, so the check and the delete run in one
  transaction that first locks the row (`SELECT … FOR UPDATE`): an upload or edit filing a
  document under it at that moment waits and then fails, rather than being deleted with it.
  The office and type that automatic PDF imports are filed under (`DocRepo:IngestionOfficeCategoryId`
  and `IngestionDocumentCategoryId`) cannot be deleted either, even before any import: the page
  says "Imported PDFs are filed here" (Codex review).
- Names are compared ignoring case and extra spaces (as master data does); changes are audited
  in the same save as before (`CategoryChanged`, now also "Deleted").
- **Missing files and System health share one check:** `IDocRepoMissingFilesService`
  (`Services/DocRepo/DocRepoMissingFilesService.cs`) checks the newest 5,000 documents (the limit
  System health already had), newest first, stops between files when cancelled, and keeps the
  result for two minutes. The page pages through that result and says when it checked, whether
  it stopped at 5,000, and has "Check again". System health's "Document Repository files" row
  uses the same result (not counting the trash, as the page by default), so both show the same
  number; forcing a System health refresh checks again. Its limits and wording are unchanged.
- Tests: `DocRepoListServiceTests`, `DocRepoMissingFilesServiceTests`, and a PostgreSQL test
  (`DocRepoListPostgresIntegrationTests`, added to the database workflow) for the `xmin` version
  and the locked delete.

## PR 7 as shipped

- **Who has which role in plain words.** Headings and tiles now read "What each role can do",
  "Roles", "Accounts with senior roles", "Senior accounts not in use" (disabled or waiting to be
  deleted) and "Roles held by one person". "Senior roles" are the roles the role catalogue marks
  as privileged (Administrator, Commandant, Head of Department); the tile names them from the
  catalogue. Permission names ("Admin.Access", "DocRepo.Purge") and stored role names moved into a
  collapsed **Technical details** panel under the table.
- The things to look at, the role descriptions (`AdminRoleDescriptorCatalog`) and the permission
  descriptions (`AdminCapabilityCatalog`) were rewritten. The media rows name the Media sources and
  Media intelligence actions they allow (some are checked inside the media services, not on the
  page); "Manage the media library" is the one no page or service checks, and says so. A test
  keeps that claim true. Keys, policy names and roles are unchanged; the finding codes
  are unchanged too. The access guide's three bullets were reworded to match.
- **Social media lists get a catalogue policy.** `AdminPolicies.SocialMediaListsManage`
  ("Admin.SocialMediaLists.Manage") is registered by the capability catalogue with exactly the
  roles the pages had (`Admin`, `HoD`). All six event type and platform pages use it, so the lists
  now appear in the table, and the policy guard test no longer needs its exception for them.
- Their entries in the Administration catalogue and the Lists used in forms hub now use the same
  policy instead of `Admin.MasterData.Manage`. Who can **open** the pages is unchanged; a HoD can
  now also see the links where the Administration catalogue is shown to them, like Activity types
  and Holidays. The Project Office Reports menu links (Admin only) are left as they were.
- Tests pin every row of the table to its policy and roles, check the new policy allows exactly
  Admin and HoD and that each social media page requires it and nothing else, and widen the
  plain-words test to this page, its permission descriptions and the role descriptions.

## PR 9 as shipped

Each mismatch was traced to its query before anything changed.

- **Media sources, "18 ready in Photos" vs "Prepared 17":** the 18 was 17 images and 1 video.
  Videos play as they are and get no thumbnails, so they are never prepared. "Prepared" also
  counted finished processing *jobs*, not images; one image can have several jobs over time, so
  that count would drift. The tiles now come from one query (`MediaLibraryFigures`,
  `Features/MediaLibrary/Admin`): "18 · ready in Photos: 17 images · 1 video" and "Images prepared
  17 of 17" (images whose thumbnails are made).
- **Media intelligence, "Images 17":** the same 17 images; the tile now says videos are not sorted.
- **Media intelligence, "Not sure: need a look 14" vs "Unsure guesses 13":** the 13 (scored too low
  to decide) was part of the 14; the other 1 was sent to a person by a safety check. The separate
  "Unsure guesses" tile is gone; "Not sure" shows "13 scored too low to decide · 1 for another
  reason". Classification review's "review" list and its count use the same definition.
- **ERP usage, "0 of 16 · 0%" with 20 min and 4 actions:** all of that use was on 20 Oct, which is
  an observed holiday (Dussehra) in the calendar. Working-day counting was right: a holiday is not
  a working day, and counting it would push the figure past 100%. The row now says "Also used on 1
  non-working day (Sunday or holiday), not counted here". A day with use or an action on a working
  day already counted, and a test now pins that.
- **ERP usage, "Never recorded" vs Users "07 May 2026":** Users shows the last sign-in; ERP usage
  counts use since counting began (14 Jul 2026). The words are now "No use since counting began",
  and when the last sign-in is older than that, the row adds "Last signed in …, before counting
  began". The "Show" filter option has the same name; its value (`never-recorded`) is unchanged.
- **ERP usage, test accounts listed with "Include service and test accounts" unticked:** the
  filter is right. Those accounts (Test HoD, Test Project Officer, the two Claude Review accounts)
  are marked as *Human* on their user records. The checkbox now reads "Include accounts marked as
  service or test" and says where the marking is set (Edit user → Account classification). No user
  data was changed; marking those accounts as Test is the owner's call.
- Routes, CSV columns and stored data are unchanged.

## PR 8 as shipped

- **System health:**
  - The rows are in plain words, for example "Database updates on record", "Sign-in key folder" and "Disk space".
  - Background jobs say "Runs every 1 day" and "Started with PRISM; it has not finished its first run yet".
  - Setting names such as `DP_KEYS_DIR` appear only in the "what to do" text, where whoever runs the server needs them.
  - The OK tile now says how many checks have run ("19 of 23 checks have run") instead of calling the unrun ones completed.
  - The groups are "Security" and "Background jobs".
- **Classification review:**
  - The review sets, the reasons and the messages are in plain words: "Sort older images again", "Photos waiting for face approval", "The two best guesses were too close".
  - The biometric confirmation wording is kept as it was.
- **Media sources:** each unavailable item's source reads "Activity photo", "Project video" and so on. The raw value stays in Technical details.
- **Add user and Edit user:**
  - "Kind of account" with "A person", "Service account" and "Test account".
  - The role panel says "What these roles can do".
  - The account-state section points to the account page.
- **Archived projects, Holidays, Celebrations and the guide:**
  - Plain headings and plain notes.
  - Celebrations drops the "Calendar source" column, which said the same thing on every row.
  - The guide no longer shows a setting name.
- **Guards:**
  - `AdminPlainWordsTests` covers these pages.
  - A new `SystemHealthMessages_UsePlainWords` test covers the health services' messages.

## PR 10 as shipped

- **One name per action:**
  - "Edit account" everywhere: the Users list, the account page and the guide.
  - "Renumber 1, 2, 3…" on Data checks, as on the lists.
  - "Legacy project import" on Data jobs.
  - Chat file storage is headed "Chat file storage" under "System".
  - The media pages sit under "System", like the sidebar.
- **Lists read the same:**
  - The lookups, Activity types and the Document Repository lists all say "Order", "In use / Not used", "Inactive" and "Kept on old records".
  - Delete appears only on values nothing uses; an in-use value says it can only be deactivated.
- **Social media platforms:**
  - Deleting a platform that events use was not checked, and the database refused it with an error page. The service now refuses it with a plain message, both when it checks and if an event is added at the same moment.
  - The platform list shows how many events use each platform.
  - The event types page no longer repeats the whole platforms list; it links to its own page instead.
- **Not done: one layout for every list.** The Document Repository lists keep the library's layout, and the social media lists keep the reports module's. These pages belong to those modules and keep their module's look, as decided in the first review (fix PR 5). Rebuilding them in the admin list layout is a larger change, left to the owner. Their words and Delete rules now match.

## PR 11 as shipped

- **Health checks keep to their time limit:**
  - The folder checks (upload folder, Document Repository folder, sign-in keys) and the free-space reads run on the thread pool with the 10-second limit. A folder on an unreachable network share now shows "The check took too long and was stopped" instead of holding the page.
  - Database checks stay inline, so an abandoned check never shares the request's database context with the next one.
- **One run of the checks at a time:** per PRISM process. Administrators who press "Run checks again" at the same moment, or whose 45-second cache expires together, wait for that run and reuse its result.
- **Legacy import:** each new preview removes staging files older than the preview lifetime plus an hour, so previews that were neither imported nor cancelled no longer pile up in `App_Data/AdminImportStaging`.
- **Audit log:**
  - Typed searches (text, a person's name, an IP) are "contains" scans, so they now keep the 30-day window. "Show all history" is one click away.
  - Picking a person, record or action still searches all history.
  - The account page's "View audit log" link asks for all history explicitly.
- **Lists:** a refused save (another administrator's change or a duplicate name) clears the tracked changes, so nothing later in the request can save them.
- **Unreadable PDFs:** the placeholder comment is replaced, and the page uses the policy constant (same policy).
