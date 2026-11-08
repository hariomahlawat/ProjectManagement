# Administration: third cold review (7 Nov 2026) and fix plan

The first two reviews and their fix PRs are in `admin-review-fixes.md` and
`admin-review-2-fixes.md`. After both plans shipped, two independent reviewers looked at the
module cold a third time:

- **UX:** all 41 admin pages, a user's page, and the Add, Edit, Reset password and Disable
  account forms. Each was checked signed in as an Admin, at desktop, at phone width (375 px) and
  in dark mode.
- **Code:** the Administration code and the pages it links to, against `AGENTS.md`.

The main findings were checked against the code.

**Verdict:**
- **Usability: 7/10** (6, then 7).
- **Code health: 7.5/10** (8, then 7.5).

The earlier fixes held: no reviewer found a regression. The score stays at 7 mainly because of
the two layout decisions left to the owner (findings U1 and U2).

**Keep:**
- navigation: Home search, Every admin page and the "How do I…" guide;
- page titles that match the sidebar;
- figures that agree across pages;
- IST times;
- warnings that say what to do;
- plain empty states;
- dark mode;
- no sideways scroll on phones;
- every admin page has a policy;
- last-admin guards;
- audited exports;
- health checks with time limits.

**Not product defects:**
- the local data has missing repository files and photos;
- no face-recognition model files are installed;
- the app runs with the Development settings;
- background jobs can show a stale last run straight after a restart.

**Rejected:** the UX review reported the Celebrations delete dialog as missing a name. It is
filled in by `admin-celebrations.js` when the dialog is opened from a row's Delete button.

## Findings

### Code
- **C1 (High): repository purge can lose a restored document.**
  - `DocRepoTrashPurgeService` deletes the file first, then removes the row by id.
  - The `Document` has no concurrency token, so nothing stops a parallel Restore.
  - If one admin purges while another restores, the restored document is deleted along with its file.
  - If the save fails after the file is gone, a later Restore brings back a document with no file.
- **C2 (Medium):** the purge worker is not safe to run twice. A second run that loads the same document throws an uncaught `DbUpdateConcurrencyException`.
- **C3 (Medium): sign-in filters miss rows.**
  - Sign-in activity keeps the newest 5,000 events, then applies the Outcome and "Only sign-ins to look at" filters, and the CSV export uses the same result.
  - Over a long period, Failed can show nothing while its tile counts 40.
- **C4 (Medium): the account-state rule is written three times.** Disabled, locked, must-change-password and active are spelled out in `UserAccountStateResolver`, `AdminUserQueryService` and `AdminDashboardService`.
- **C5 (Low): legacy import's audit is separate from the import.**
  - The audit record is written after the import commits, so an audit failure reports "could not be completed" although the projects were imported.
  - Staging files live under the app folder, so a preview cannot be committed by a different process during an overlapping deployment.
- **C6 (Low): audit log figures and search.**
  - "N records changed" counts events, not distinct records.
  - Text search misses "R&D", apostrophes and Hindi, because the details JSON escapes them as `\uXXXX`.
- **C7 (Low): Home's numbers and names.** Home's "7 days" is a rolling UTC window, while Sign-in activity uses IST days. Home's recent changes show user names instead of the shared name lookup.
- **C8 (Low): data-job leftovers.**
  - Unreadable PDFs has no paging and queries in the PageModel.
  - The Recycle bulk loop sits in the PageModel.
  - Disable maps exceptions by their English text.
  - The PDF failure CSV is the one export without an audit record.
  - PDF run history is kept in memory per process.

### UX
- **U1 (High): the reference lists come in five designs.**
  - The lists are the lookups, categories, Activity types, ARPP values, the Document Repository lists and the social media lists.
  - They differ in tiles, filters, buttons, wording and the Delete rule.
  - For example, an unused lookup can only be deactivated, while an unused social platform can be deleted.
- **U2 (High): some pages leave the admin layout.** The media pages, the Document Repository admin pages, the social media lists and Document deletion approvals (the Decision Centre) drop the Administration header and sidebar; the way back is the breadcrumb.
- **U3 (Medium): System health's background jobs.**
  - The rows show raw job text, such as "Daily aggregate for 2026-11-06 already exists." and "Purged 0 notebook trash item(s).".
  - The job is named "Login aggregation".
  - The repository trash purge reads as running every 6 hours, while the trash page says automatic clean-up is off.
- **U4 (Medium):** Restore deleted items shows briefing decks in its recent activity but cannot restore them.
- **U5 (Medium): people and roles.**
  - The audit log shows roles as "Admin,HoD,Project Officer".
  - Home and a user's page show some people by user name.
  - The tile "8 Actors · 3 records changed" is unclear.
- **U6 (Medium): bins on phones.** The active tab sits off-screen in the scrolling tab strip, so nothing visible names the list.
- **U7 (Medium):** Edit user, Reset password and Disable account still use formal wording ("This operation…", "approved secure channel").
- **U8 (Low): smaller wording and figures.**
  - Missing files shows a time without a zone and says its result is kept "two minutes", where System health says 45 seconds.
  - Holidays uses "RH · Office holiday", and its actions are mismatched ("Declare as office holiday" / "Withdraw office observance").
  - The Data checks confirmation says "I looked at what was found."
  - Activity types shows a seed timestamp by "system" and has no Order column.
  - Classification review shows "Strength 37% … Lead 1%", and has a duplicate Overview button.
  - Media sources has a banner that reads as a contradiction.
  - Users has a "Password change" tile and a "Last activity" column.
  - On phones, the lookup tables put Edit and Deactivate far to the right.

## Fix plan

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **Purge cannot lose a document.** The file is moved to quarantine, the row is deleted only if it is still in the trash (concurrency check), and the file is removed only after the commit; the worker skips an item another run took; tests for purge racing restore, a failed save and two overlapping runs | C1, C2 |
| 3 | **Sign-in activity counts what it shows.** Outcome and review filters applied in the query before the cap; the export uses the same filtered set; Home's 7 days on IST days; Home shows people through the shared name lookup | C3, C7 |
| 4 | **One account-state rule.** One set of predicates for disabled, locked, must-change and active, used by the state resolver, the Users query and Home | C4 |
| 5 | **Audit log names and figures.** Full role names and people's names in the Actor column; "records changed" counts distinct records; new audit details stored without escaping letters (older rows stay as they are, and search says so) | C6, U5 |
| 6 | **System health's jobs in plain words.** Each job's last-run message rewritten at its source ("Nothing to remove", "Sign-in summary for 6 Nov is up to date"); "Sign-in summary" instead of "Login aggregation"; the repository purge shows "Switched off" when retention is 0; Missing files shows IST and the right cache time | U3, U8 (part) |
| 7 | **Plain words, round three.** Edit user, Reset password and Disable account; Holidays; the Data checks confirmation; Activity types; Classification review cards; the Media sources banner; Users tiles and columns | U7, U8 |
| 8 | **Phones.** The active bin tab scrolls into view and the heading names the list; lookup rows show their actions without sideways scrolling | U6, U8 (part) |
| 9 | **Data-job tidy-up.** Legacy import audit written with the import; staging under the data root (old location still read); Unreadable PDFs paged through a query service; the Recycle loop in a service; Disable gets typed results; the PDF failure CSV audited; PDF run summaries kept in the database | C5, C8 |

Rules carried over:
- every PR keeps routes, policies and data contracts;
- every PR ships with tests and is checked on screen while signed in;
- every PR is merged when CI and the Codex review are clean.

PR 9 moves the legacy-import staging folder. It must keep reading the old folder for one release, so that a preview made before the deployment can still be committed.

## PR 2 as shipped

`DocRepoTrashPurgeService.PurgeAsync` (Services/DocRepo), used by the Trash page and the
retention worker, now works in three steps:
1. It moves the file to `<DocRepo:RootPath>/purge-quarantine/<document id>-<attempt>.pdf`
   (new `IDocStorage.MoveAsync`; a rename on the same disk).
2. In one transaction it deletes the row only while it is still in the trash (and, for the
   worker, still past the cutoff), with a conditional `ExecuteDelete`, and saves the `Purged`
   audit entry. The database's cascades remove the dependent rows, as before.
3. Only after the commit does it delete the set-aside file.

If no row was deleted (restored meanwhile, or taken by another run), or the save fails, the
file is moved back; if another run already purged the row, the set-aside copy is deleted
instead. A purge that loses the race returns NotFound, so the worker skips it rather than
throwing. No migration, route, policy or audit change. Tests: `DocRepoTrashPurgeRaceTests`
(SQLite and the real file store) cover purge racing a restore, a save that fails after the
move and two overlapping runs.

Left as is: if the process dies between the move and the commit, the file stays in
`purge-quarantine` with the document id in its name, so an operator can move it back by hand.
A move back that fails is written to the error log with both paths.

## Owner decisions

These are larger changes. None of the PRs above makes them without the owner's go-ahead.

1. **One layout for all 12 lists (U1).** Recommended: one shared list page, with tiles, a table, Add, Edit, Deactivate, and Delete only when unused. The Document Repository and social media lists would leave their module's look for the admin one. This is about three PRs.
2. **Outer pages inside the admin layout (U2).**
   - **Option A:** show the media, Document Repository and social media admin pages in the admin shell, with sidebar and header. This is about three PRs.
   - **Option B, cheaper:** keep their module look and add the same Administration header strip and back link to all of them.
   - Document deletion approvals stays in the Decision Centre either way, but gets a heading that names it.
3. **Briefing decks in Restore deleted items (U4):** list them as a kind, or link to where they are restored.
4. Still open from the second review:
   - Test HoD, Test Project Officer and the two Claude Review accounts are marked as people, so ERP usage counts them.
   - Documents restored before second-review PR 2 stay hidden from readers and search.

### Decided (8 Nov 2026)

The owner took the recommendations:
- one shared list page (1);
- option A, the outer pages inside the admin layout (2);
- briefing decks listed in Restore deleted items (3).

For the two items still open from the second review (4):
- **Test accounts:** marking the four accounts as Test is an owner action in Edit user → "Kind of account". No code change and no data script.
- **Hidden documents:** documents restored before second-review PR 2 get a review list with Reactivate. Nothing is reactivated automatically, because some of those documents may have been made inactive on purpose.

**The reference lists.** The admin ones:
- Project categories, Technical categories, Project types, Sponsoring units and Line directorates;
- Activity types;
- the ARPP value lists.

The Document Repository's lists: Document types and Offices. The social media lists: Event types and Platforms.

Holidays and Celebrations are not plain lists (dates, recurring rules), so they keep their own pages.

**One rule for every list:**
- tiles (All, In use, Inactive);
- one table with Name, Order, Used by, Status and Actions;
- Add and Edit, and Deactivate or Reactivate;
- Delete only when nothing uses the item. The service checks use, and the database's foreign keys stay as the backstop.
- Inactive items stay on old records and drop out of new forms.

| PR | Scope | Decision |
| --- | --- | --- |
| 10 | **Shared list page.** One partial and view model, plus one usage-and-delete rule in a service; applied to Project types, Sponsoring units and Line directorates (they already share `_AdminFlatLookupDirectory`) | 1 |
| 11 | **More lists on the shared page.** Project categories, Technical categories, Activity types and the ARPP value lists | 1 |
| 12 | **Document Repository and social media lists in Administration.** Document types, Offices, social media Event types and Platforms move onto the shared list page, inside the admin layout (sidebar, header, Administration breadcrumb) | 1, 2 |
| 13 | **Media pages in Administration.** Media sources, Media intelligence and Classification review render in the admin layout | 2 |
| 14 | **Repository trash and Document deletion approvals.** Repository trash renders in the admin layout. Document deletion approvals stays in the Decision Centre, with a heading that names it and a link back to Administration | 2 |
| 15 | **Briefing decks in Restore deleted items.** Deleted decks listed as a kind, restored through the existing briefing-deck service, with its policy and audit | 3 |
| 16 | **Restored but still hidden documents.** A Document Repository admin list of documents that were restored before second-review PR 2 and are still inactive, each with Reactivate (audited, reindexed); no bulk change without a confirmation | 4 |

Rules:
- Old URLs keep working (redirect where a page moves) and policies are unchanged.
- Every PR ships with tests and is checked on screen at desktop and phone width, light and dark, while signed in.
- Every PR is merged when CI and Codex are clean.
- PRs 10–12 run in order, because they share the list page. PRs 13–16 are independent of them and of each other.

## PR 3 as shipped

- **Sign-in activity (C3).** `AdminLoginMonitoringService` turns the Outcome filter and the review rule into the database query before the newest-5,000 cap. The review rule for a successful sign-in (between 22:00 and 05:00 IST) is `LateNightSuccessfulSignIn`, kept in step with `BuildEvent`; failed and locked-out attempts always need a look. The chart and the CSV export read the filtered set, and the "Sign-ins to look at" queue and its tile always read the review set, so an older failed sign-in is listed and counted even when newer successful ones fill the cap. "Too many to draw" now means a list on the page was cut off, and "Sign-ins in this period" shows the full count instead of the capped one. The usual sign-in times still come from the newest 5,000 events in the period.
- **Home (C7).** `AdminDashboardService` counts its 7 days as today and the six IST calendar days before it, the same period as Sign-in activity's 7 days. Recent changes show people through `AuditActorNames`, as the audit log does: full name, then user name, and "PRISM" for rows the application wrote. The account-state counts are left for PR 4.
- Tests: `AdminSignInCountsWhatItShowsTests` (filtered outcome and review rows older than the cap, export matches the list, the review rule translates to PostgreSQL, Home's IST window at a day boundary, Home's display names).

## PR 4 as shipped

**Shipped.** `UserAccountStateRules` (Services/Admin) is the one account-state rule: pending
deletion, then disabled, then temporarily locked (lockout ends after now), then must change
password, then active. Each state is one predicate, written once, used two ways: as an
EF-translatable `Expression<Func<ApplicationUser, bool>>` for a given now (bound as a query
parameter, not inlined), and compiled for use in memory.

- `UserAccountStateResolver` reads the state from the rule; its names, badges and sign-in flags
  are unchanged.
- The Users list's status filters, tiles and export filter through the rule. The tiles are now
  one count query per state instead of one grouped query.
- Home's account tiles count through the rule. Home's "Active" is now counted directly instead
  of total minus the other four; the result is the same.
- The three copies agreed before this change, so no count moves. `AccountStateRuleTests` pinned
  the old results first (lockout ended, lockout ending exactly now, no lockout, disabled and
  locked together, pending deletion with every other flag) and the refactor kept them; it also
  checks that each predicate translates for PostgreSQL.

### PR 6 shipped: System health's jobs in plain words

- Every background job writes its last-run line through one helper (`Services/Admin/BackgroundJobMessages.cs`): "Nothing to remove", "Removed 3 items from the notebook bin", "Sign-in summary for 6 Nov is up to date". Dates read "6 Nov".
- "Login aggregation" is shown as "Sign-in summary". Its key `login-aggregation` is unchanged.
- A job turned off in the settings shows "Switched off", with no schedule and no warning. This applies to the Document Repository bin clean-up when `DocRepo:TrashRetentionDays` is 0, to audit and notification retention when they are off, and to chat attachment clean-up while attachments are off.
- Missing files shows its check time in IST with the zone. Its cache time is read from the check itself: 2 minutes, here and on System health. System health's own 45-second page cache is separate.
- Job status is kept in memory, so the old wording disappears when PRISM restarts with this version.

### PR 7 shipped: plain words, round three

- **Account forms (U7).** Edit user, Reset password and Disable account say what happens in short words: "They are signed out [within a minute] and must choose a new password the next time they sign in" (the delay comes from the sign-in check interval), "Keep the password safe", "Give it to them only in an approved, safe way, such as in person". Every warning keeps its meaning. "Account classification" is now "Kind of account" on Add and Edit user, and ERP usage points there. Disable account changed only in its view; its page model is PR 9's.
- **Holidays.** An observed restricted holiday shows "Office holiday" (its Classification column still says Restricted Holiday), and an unobserved one "Office open". The actions are a matched pair, "Make it an office holiday" and "Stop office holiday", on the list, on their own pages, in the service's messages and in Help. Routes (`./Observe`, `./WithdrawObservance`) and audit action names are unchanged. The shared calendar keeps its own "RH · Office holiday" label.
- **Data checks.** The confirmation reads "I have checked the problems above. Renumber this list 1, 2, 3…, keeping its order."
- **Activity types.** Seeded types that were never edited show "Built in" instead of the seed time and "system". Activity types have no stored order (forms list them by name), so the list says "Listed A to Z, as they appear in activity forms" instead of showing an Order column. A real order would need a new column and migration; that belongs with owner decision 1 (one layout for all lists).
- **Classification review.** Cards show "Score 41 of 100" and "Best guess ahead by 8 points" (the gap between the two scores as shown; "Less than 1 point" when they round the same), with the reason underneath. The "Privileged access selected" warning in the role picker now reads "A role with wide access is ticked. Check they need it before saving." The duplicate Overview button is gone; the Overview tab stays.
- **Media sources.** The off banner reads "Photos shows only what is stored in PRISM. Reading photos and videos from other folders is switched off…", and the add-folder card says a folder cannot be added yet. The out-of-date database banner no longer says "operational" next to "requires repair".
- **Users.** The tile and filter read "Must change password", the column "Last signed in" (it shows the last successful sign-in), and a user's page "Must change password: Yes, at their next sign-in / No".
- Tests: `AdminPlainWordsTests` (the old phrases stay gone, the holiday pair, scores as points, Built in).

### PR 5 shipped: audit log names and figures

- **Actor column (U5).** The audit log and its details drawer show roles in full through `IAdminRoleDescriptorCatalog` ("Administrator, Head of Department, Project Officer" instead of "Admin,HoD,Project Officer"). People were already resolved through `AuditActorNames`, the lookup Home uses since PR 3: full name, then user name, and "PRISM" for rows the application wrote.
- **"Records changed" (C6).** The tile reads "8 people · 3 records changed". It counts distinct records, one per entity type and id, however many events name them; events that name no record are left out. The count runs in the database with plain string functions over the stored details.
- **Search (C6).** `AuditService` and `AdminAuditService` now store details without escaping letters (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`), so search finds "R&D", apostrophes and Hindi in new entries. The drawer's raw JSON shows older rows' letters as written too. The stored JSON is never rendered as markup: Razor encodes it, and the drawer embeds it through a second serialisation that still escapes `<`, `>`, `&` and `'`, and sets it with `textContent`.
- Older rows are not rewritten and still hold `\uXXXX` codes, so search can miss those letters in them. A short hint under the search box says so.
- No migration; routes, policies and the CSV export are unchanged.
- Tests: `AdminAuditLogNamesAndFiguresTests` (full role names and display names, the distinct-record count and its PostgreSQL translation, search for "R&D", an apostrophe and Hindi, the drawer JSON staying HTML-safe, older escaped rows reading as written).

### PR 8 shipped: phones

- **Restore deleted items (U6).** On a phone (640 px and narrower) a line above the tab strip reads "Showing: Calendar events" (the open tab's own label), so the open list is named even before the strip scrolls. On load, `admin-shell.js` brings the open tab into view with `scrollIntoView({ inline: "center", block: "nearest" })`, and only when the strip is wider than the screen, so the page itself does not move. Desktop is unchanged; the line is hidden there and screen readers keep the tab's `aria-current`.
- **Lookup lists (U8).** The shared lookup table (`_AdminFlatLookupDirectory`: sponsoring units, line directorates, project types) and Activity types become one card per row at 575 px and narrower: the name across the top, the ⋯ actions menu (Edit, Deactivate) pinned to the card's top right, and Usage, Status, Order and Last changed labelled underneath, two to a line. No sideways scrolling. The desktop table is unchanged. The block (`.admin-data-table--cards`) follows the Users card layout and uses only admin tokens, so dark mode follows. It also clears the desktop table's `min-width` (52rem on the lookup table), which would otherwise make each card 832 px wide.
- Checked on screen while signed in, 8 Nov: 375 px and 1400 px, light and dark. No page scrolls sideways at 375 px; the open bin tab is centred in its strip and the page stays at the top; the ⋯ menu opens in view.
- **Already fine on phones, checked and left as they are:** ARPP values (each row stacks, Edit and Deactivate under the value), the social media types and platforms (cards with their buttons), and the Document Repository lists (`_DocRepoListTable` stacks below 768 px with Save, Deactivate and Delete under each row).
- Routes, handlers, form names and policies are unchanged. No new scripts or packages.
- Tests: `AdminPhoneListsTests` (the card class and labels on both tables, the actions pinned in view, token-only CSS, the tab label and the script hook) and two cases in `admin-shell.test.js` (the open tab is scrolled into view with `block: "nearest"`; a strip that fits is left alone).

### PR 9 shipped: data-job tidy-up

- **Legacy import's audit (C5).** The projects, the import record and the audit entry are saved in one transaction. `LegacyImportPreflightService.CommitAsync` opens it, and `ProjectImportService` joins it through a savepoint (`RelationalTransactionScope`). If the audit write fails, nothing is imported and the message says so. Used on its own, the import still opens its own transaction.
- **Legacy import staging (C5).** New previews are staged in `admin-import-staging` under the upload root (`ProjectPhotos:StorageRoot` or `PM_UPLOAD_ROOT`), the folder every PRISM process shares. OCR's work files are kept there by default too. For one release the old folder, `App_Data/AdminImportStaging` under the app folder, is still read, imported from, cancelled from and swept, so a preview checked just before the deployment can still be imported. A later release removes the old folder from `StagingRoots()`.
  - Never inside the web root: if the upload root is not configured it defaults to `wwwroot/uploads`, which is served without sign-in, so previews then stay in the private `App_Data/AdminImportStaging` folder.
  - Deployment: the PRISM service account must be able to write to the upload root. It already does, for uploads.
  - Rollback: the previous release reads only the old folder. A preview checked on this release is then not found, and the user checks the file again. Nothing is imported twice: the unique category index still guards that.
- **Unreadable PDFs (C8).** The page lists through `IOcrFailureQueryService` (Services/DocRepo), 25 rows a page with 10/25/50/100 to choose, newest first, and the library's pager. The policy is unchanged (`Documents.DeleteApprove`). "Read again" keeps the page you are on.
- **Restore deleted items (C8).** The bulk restore and permanent-delete loop moved from the page into `IDocumentRecycleService` (Services/Admin/Recovery). It still acts only on documents that are still in the bin, carries on past one failure, and a repeated request finds nothing left to do. The page keeps the checks on what was asked for (the selection, the limit, typing DELETE) and the words.
- **Disable account (C8).** `IUserLifecycleService.TryDisableAsync` returns `UserDisableOutcome` (Disabled, AlreadyDisabled, NotFound, SelfDisable, PendingDeletion, LastActiveAdmin). The page picks its words from the outcome instead of matching English exception text. `DisableAsync` keeps its old behaviour for other callers.
- **PDF failure CSV (C8).** The failure report writes an `AdminExport.PdfIngestionFailures` audit record ("PDF copy failures exported", under Exports) before the file is returned, like the other admin exports. If the audit cannot be written, no file is returned.
- **PDF run history (C8).** Run summaries are kept in the new `PdfIngestionRuns` table, written by hand as migration `20261226080013_AddPdfIngestionRuns`, because the model snapshot lags. Each process now shows the same history, and it survives a restart. The newest 50 runs are kept. Saving the same run again replaces its row, and a failure to save a summary is logged without changing the run's result.
  - Deployment: the change only adds a table, so a server still on the previous release ignores it.
  - Rollback: the old release goes back to its in-memory list and leaves the table unused.
  - Wording that said the history starts again when PRISM restarts now reads "Not run yet" (Copy PDFs), "No PDF run yet" (Home) and "No run yet" (Data jobs).
- Tests: `AdminDataJobTidyUpTests` covers the import rolled back when its audit fails, staging under the data root, importing from and sweeping the old folder, paging, the policy, the recycle service, the Disable words and the run history across contexts and its trimming. `UserLifecycleServiceTests.TryDisable_ReturnsATypedOutcomeForEverySafeguard` covers the typed outcomes, and `AdminCodeGuardsTests` covers the CSV audit and that no file is returned without it.

### PR 13 shipped: media pages in Administration

**Shipped.** Owner decision 2, option A. Media sources, Media intelligence and Classification review now open inside Administration, with its sidebar, page header, "System" eyebrow and the "Administration" context bar on a phone, instead of leaving for the module layout.

- `AdminNavigationCatalog`: the three entries no longer set `usesAdminShell: false`. Media intelligence and Classification review stay out of the sidebar and now name Media sources as their `SidebarParent`, so it stays highlighted and Every admin page says "Opens from Media sources".
- The pages use the shared `_AdminPageHeader` instead of their own header and drop their `container-*` wrappers, so the shell's spacing applies. Their own page styles stay (`media-sources.css`, `media-intelligence.css`). On Classification review the confirmed "Sort older images again" button sits at the right of the tabs, because the shared header has no confirmed-POST action.
- One layout, not two. These pages are Administration pages (their routes are under `/Admin`, their policy is `Admin.Media.View`), even when opened from Photos. The layout's existing rule decides the shell: administrators get it, and an HoD who can open the pages sees the same header in the standard layout, as on every other admin page an HoD can open. No "came from Photos" switch.
- The "Administration ›" return bar is no longer shown on these pages: the shell already leads back. It still serves the pages that keep their module's layout (Document Repository, social media lists, Document deletion approvals). Every admin page no longer says media opens in "the media library".
- Routes, handlers, policies and the Photos links are unchanged.
- Tests: `AdminMediaPagesInShellTests` (in scope for the shell, no return bar, "System" eyebrow, sidebar parent, no "own module" note, the shared header in the views); `AdminNavigationCatalogTests` and `AdminPageIndexTests` updated.

### PR 10 shipped: one shared list page

**Shipped.** Owner decision 1, first step. Project types, Sponsoring units and Line directorates use the shared list page. PRs 11 and 12 move the other lists onto it.

- **One rule in a service.** `IReferenceListService` (Services/Admin/MasterData/ReferenceLists) counts use once, for the tiles, the Used by column and Delete. A list is described by a `ReferenceListDefinition`: its table, and which records use it (`ReferenceListUsageSource`, one per kind of record, so a list can be used by projects and by activities). `ReferenceListCatalog` holds the three lists.
- **What counts as use.** Projects (every project row, archived and deleted ones too, because the foreign key counts them) and project change requests, decided ones included. A change request names the item it started from in a column and the item it proposes inside its JSON payload, with no foreign key behind either, so the service reads the payload in memory. A source is either a database query or such an in-memory loader.
- **Delete only when nothing uses the item.** The service refuses an item in use with "EntityInUse" and says to deactivate it instead. The database's foreign keys stay as the backstop: a refused delete leaves nothing changed. A delete is audited (`MasterData.ProjectTypeDeleted`, `…SponsoringUnitDeleted`, `…LineDirectorateDeleted`) and closes the gap in the order. These three lists had no Delete before.
- **The page.** `_AdminReferenceList.cshtml` with `AdminReferenceListModel`: tiles All, In use and Inactive, which also filter the table; one table of Name, any extra columns, Order, Used by, Status and Actions (Edit, Deactivate or Reactivate, Delete). Delete asks in the shared confirm dialog; for an item in use it is shown locked with "In use, so deactivate it instead". On a phone each row is a card with its actions at the top right (PR 8's layout).
- **Extra columns.** A definition can add columns between Name and Order (`ExtraColumns`, `LoadExtraColumns`), for a code or a parent in PRs 11 and 12.
- **Pages.** The three Index pages derive from `AdminReferenceListPageModel`, which holds the handlers (Move, RenumberOrder and the new Delete) and the query names (`q`, `status`, `pageNumber`, `pageSize`). Each page keeps its own policy. Routes, Create, Edit and Deactivate are unchanged.
- **One change of default.** The list opens on All instead of Active, so the All tile is the landing view. `?status=active` still works.
- Removed: `_AdminFlatLookupDirectory.cshtml`, `AdminFlatLookupDirectoryModel` and the query service's `GetFlatLookupAsync`, which only these pages used. The "Last changed" column is gone; the audit log keeps the history.
- No migration.
- Tests: `AdminReferenceListTests` (items used by a project or named by a change request refuse Delete, unused ones are deleted and audited, an old version and a missing permission are refused, tile counts and filters, the three pages render the shared layout); `AdminSearchEscapingPostgresTests` now searches through the shared service on PostgreSQL.

### PR 11 shipped: more lists on the shared page

**Shipped.** Owner decision 1, second step. Project categories, Technical categories, Activity types and the ARPP values (CFA, Funds, DFPDS schedules) use the shared list page: tiles All, In use and Inactive; one table of Name, Order, Used by, Status and Actions; Delete only when nothing uses the item.

- **What counts as use, per list** (each a `ReferenceListUsageSource` in `ReferenceListCatalog`):
  - Project categories: projects, sub-categories, legacy imports, and change requests (the category a request started from, `OriginalCategoryId`, and the one it proposes in its JSON payload). Change requests were not counted before, so Delete could fail at the database or break a pending request.
  - Technical categories: projects, sub-categories, legacy imports, and change requests (`OriginalTechnicalCategoryId`, the proposed `TechnicalCategoryId` column and the payload). Only the column was counted before.
  - Activity types: every activity, deleted ones too (the foreign key counts them). Activity types had no Delete before.
  - ARPP values: every ARPP entry that links to the value. The entry's link is emptied when its value is deleted, so the database would not stop the delete; this count is the only guard. The ARPP values had no Delete before.
- **Trees.** A definition can name its parent column (`ParentProperty`). The page then lists each parent before its children (`ReferenceListTree`), indents children one step per level, and moves an item only among its siblings (the existing Move commands). With a search or a tile filter the rows are not indented and each child says "Under Defence / Electronics". Searching a parent's name finds its children. Deleting closes the gap among the item's siblings only. A cycle or a missing parent in old data is listed once, at the top level. Rows offer "Add sub-category".
- **Lists that differ.** A definition can give its own projection: the DFPDS code is the name, and a description is a line under the name (activity types, DFPDS schedules) that search also reads. Activity types have no stored order, so there is no Order column and the list reads A to Z. The ARPP order is the number people type: it is shown as typed, and a delete does not renumber the rest.
- **Deactivate.** Categories, activity types and ARPP values deactivate in place (the `Toggle` handler, with the state asked for and the row version shown) instead of a Deactivate page. Activity types gained that safety: a resubmitted form no longer flips a type back; an old form without a state still flips it, as before.
- **ARPP page.** One list at a time under a tab strip (`?list=cfa|funds|dfpds`, CFA by default) with "Add CFA" (or Fund, DFPDS schedule) beside it. Add and Edit stay in the page's dialog (Save handler); the old SetActive handler still works.
- **Kept.** Routes, policies (`Admin.MasterData.Manage`; activity types `Admin.ActivityTypes.Manage`), the Create and Edit pages, the Move, Toggle, Save and SetActive handlers. The category Delete pages (`/Admin/Categories/Delete/{id}`, `/Admin/TechnicalCategories/Delete/{id}`) still work and now follow the shared rule.
- **Removed.** `_AdminCategoryDirectory.cshtml`, its model and `admin-category-tree.js` (expand and collapse); the ARPP `_ReferencePanel.cshtml`; the query service's `GetCategoriesAsync`, `GetCategoryImpactAsync` and `GetActivityTypesAsync`; the command service's category deletes (now `IReferenceListService.DeleteAsync`). Activity types lose the "Last changed" column and "Built in", as the PR 10 lists did; the audit log keeps the history. The lists open on All instead of Active.
- **Check and delete in one step (Codex finding).** `DeleteAsync` now opens its transaction first and, on PostgreSQL, locks the item's row (`FOR UPDATE`) before counting use. A record saved at the same moment that points at the item either commits first and is counted, or waits and is then refused by its foreign key; an ARPP entry can no longer lose its value to a delete that ran between the check and the save. `AdminSearchEscapingPostgresTests` covers it (run in `database-migrations.yml`).
- New audit actions: `MasterData.ActivityTypeDeleted`, `MasterData.ArppReferenceDeleted`.
- No migration. Rollback: the previous release has the old pages; nothing stored changes.
- Tests: `AdminReferenceListMoreListsTests` (tree order, indent, sibling moves and the sibling gap; sub-categories, legacy imports and change requests count; technical change-request column, original and payload; activity types A to Z with deleted activities counted, description search, the activity types permission; ARPP entries block Delete and an unused value is deleted without renumbering; each page renders the shared layout with its own policy; old handlers and routes stay). `AdminMasterDataCategoryCommandTests` and `AdminCodeGuardsTests` now go through the shared rule and tree.

### PR 14 shipped: repository trash in Administration

- **Repository trash (decision 2, option A).** The Document Repository's deleted documents (`/DocumentRepository/Admin/Trash`) render inside the admin layout: sidebar (with Restore deleted items lit), mobile header, the "Restore & archive" eyebrow and the "Restore deleted items" heading, like the other bins. The page shows the Restore deleted items tabs with "Repository documents" selected, and that tab and the overview card no longer say the page opens in another module. The Documents tab in the top bar is no longer lit there.
  - Paging, search, Restore and Delete for good are unchanged, as are the route, the handlers and the policy (`DocRepo.Purge`, Admin only). The library's "Deleted repository documents" menu item still opens the page.
- **Document deletion approvals.** Stays in the Decision Centre. Opened with the repository-deletions filter (`/Approvals/Pending?Type=RepositoryDocumentDelete`), its heading reads "Document deletion approvals" with a sentence on what approving does, and the page title matches. The "Administration ›" return bar above it, shown to administrators, is the link back. Without that filter the Decision Centre is unchanged.
- No migration; no new route.
- Tests: `AdminRepositoryTrashInShellTests` (in the shell, eyebrow, the rest of the repository keeps its layout, the page keeps its actions, the Decision Centre heading with and without the filter); `AdminRestoreTabsTests`, `AdminNavigationCatalogTests` and `AdminPageIndexTests` updated.

### PR 15 shipped: briefing decks in Restore deleted items

- **A new kind.** Restore deleted items has a "Briefing decks" tab, between Calendar events and Chat files, and an overview line: "Kept 30 days after they are deleted; after that they cannot be restored." The page is `/Admin/BriefingDecks/Deleted` ("Deleted briefing decks" in search). Each row shows the deck, its projects, who made it, who deleted it and when, the last day it can be restored, and Restore.
- **One rule for what can come back.** `ProjectBriefingDeckQueries.Restorable` (deleted within `ProjectBriefingDeckService.DeletedRetentionDays`, 30 days) is used by Studio's "Recently deleted", the service's restore and the admin count (`AdminBins.DeletedBriefingDecks`). A deck past the window is not listed or counted even before it is removed for good, which happens the next time any deck is deleted. Home's restore tile adds the new tab's count in, as it does for the others.
- **Restore goes through the service.** The page calls `IProjectBriefingDeckService.RestoreAsync`, so the window, the "(restored)" rename when another deck has taken the name and the `ProjectBriefing.DeckRestored` audit entry are the service's own. The audit entry names the administrator who restored it. The service's list now also returns the owner's name (an optional field; Studio does not use it).
- **Policy.** The page and its Restore handler use `Admin.Recovery.Manage` (Administrator), like the other Restore deleted items pages, because an administrator restores other people's decks. Studio's `ProjectBriefingDecks.Manage` (Comdt and HoD) does not include administrators and is unchanged, as are Studio's routes and handlers.
- Restore is safe (the deck comes back as it was), so it has no confirmation step, as in Studio. There is no delete-for-good button: decks past their window are removed by the service.
- No migration. Rollback: the previous release has no tab or page; deleted decks stay restorable in Studio.
- Tests: `AdminBriefingDecksRecoveryTests` (listing with owner, a deck past the window not listed or counted, the tab count matching the list, the page's policy and that only Administrator passes it, restoring another person's deck with the service's audit entry and name rule, a deck past the window not restored).

### PR 16 shipped: restored but still hidden documents

- **What is listed.** Before second-review PR 2, restoring a repository document from the trash cleared its delete details but left it inactive, so readers and search could not see it. `DocRepoRestoredHiddenService` (Services/DocRepo) lists a document when it is not deleted, is inactive, has a `RestoredFromTrash` entry in the repository audit (`DocRepoAudits`), and nobody deactivated it after its latest restore. The trash page has written `RestoredFromTrash` for every restore, before and since PR 2, so it is the reliable mark. No date cut-off is needed: a restore since PR 2 makes the document active, so it only appears if it was hidden again by some other route.
- **Helping the admin judge.** Each row shows who restored the document and when (IST), and links to the document. A document whose last switch before the restore was a deactivation (Manage → Deactivate) is marked "Made inactive on purpose", with who and when.
- **Reactivate.** Nothing changes automatically. Reactivate on a row, or "Reactivate all shown" behind the PRISM confirm dialog, sets `IsActive = true` and saves an `Activated` audit entry (with `source: "Restored but still hidden"`) in the same save. Only documents still on the list change; anything reactivated, deleted or deactivated meanwhile is left alone and counted in the message. The re-check and the change run in one transaction with the document rows locked first (`FOR UPDATE`), so a Deactivate made at the same moment either finishes first and is seen, or waits and wins afterwards (Codex finding; `DocRepoRestoredHiddenPostgresIntegrationTests`, run in `database-migrations.yml`). The page shows at most 200 rows, newest restore first, and says so when there are more; one request reactivates at most 200, so "Reactivate all shown" stays well under ASP.NET Core's limit of 1,024 form values (second Codex finding). The Search V2 trigger on `Documents` queues the reindex.
- **Where.** `/DocumentRepository/Admin/RestoredHidden`, policy `DocRepo.Purge` (the Repository trash's), checked on the page and in the service. It renders in the admin layout and is in the catalogue under Restore deleted items, next to Deleted repository documents. The Repository trash shows a line linking to it while the list is not empty (`RestoredHiddenNotice` view component).
- On phones each document is a card with Reactivate full width, so nothing scrolls sideways. Checked on screen at desktop and phone width, light and dark, against a throwaway copy of the dev database with three simulated pre-fix restores (one marked "made inactive on purpose"): one Reactivate, then Reactivate all through the confirm dialog, then the empty list and the trash line gone.
- No migration and no data script; routes and policies unchanged.
- Tests: `DocRepoRestoredHiddenServiceTests` (which documents are listed, including a deliberately inactive one that was never restored and one hidden again after its restore; the "made inactive on purpose" mark; reactivate and its audit; reactivate all; refusal without the policy, in the service and on the page; the catalogue entry).

### PR 12 shipped: repository and social media lists in Administration

**Shipped.** Owner decisions 1 and 2, last step. Repository document types, Repository offices, Social media event types and Social media platforms use the shared list page and render inside Administration: sidebar ("Lists used in forms" lit), header with the "Reference lists" eyebrow, and the "Administration" context bar on a phone. Every reference list now has the one layout and the one Delete rule.

- **What counts as use, per list** (each a `ReferenceListUsageSource` in `ReferenceListCatalog`):
  - Document types and offices: every document, trashed ones too (they can be restored, and the database would delete them with the value: the link cascades). The type and office that automatic PDF imports are filed under (`DocRepo:IngestionDocumentCategoryId`, `…OfficeCategoryId`) count as "1 setting for imported PDFs", so they show as in use and cannot be deleted; before, Delete was offered and then refused.
  - Event types and platforms: every event (the foreign key refuses the delete). Platforms already refused Delete when events used them (second review, PR 10); that check is now the shared rule, and event types get the same.
- **Lists keyed by Guid.** The social media lists are keyed by Guid, so the shared rule is now typed by the list's key: `KeyedReferenceListDefinition<TKey>`, a generic engine behind `IReferenceListService`, and `AdminReferenceListPageModel<TKey>`. Rows carry the id as text for the forms. `IReferenceListService` gains Guid overloads of `DeleteAsync` and `GetUsageAsync`; the int ones are unchanged.
- **Check and delete in one step.** PR 11's lock-before-count applies to all four: the transaction opens, the item's row is locked (`FOR UPDATE`), then use is counted. For the repository lists this matters most: a document filed at the same moment either commits first and is counted, or waits and is then refused, instead of being deleted along with its type or office.
- **Repository lists keep their ways.** Their row version is PostgreSQL's xmin (no RowVersion column), carried as four bytes like every other list's version; a Delete from an out-of-date page is refused. Their audit stays in the repository's own trail (`DocRepoAudits`, "CategoryChanged … Deleted"), as their add, rename and switch-off do; nothing is written twice. Their order is the number people type: shown as typed, and a delete leaves the rest alone (Data checks offers Renumber). Add and Edit (name and order) open a dialog on the page, going through `DocRepoListService` as before.
- **Social media lists** are listed A to Z with their description under the name, Deactivate in place (the state asked for, never a flip), and Add and Edit on their own pages, which also moved into the admin layout.
- **Kept.** Routes, policies (`DocRepo.ManageCategories`, `Admin.SocialMediaLists.Manage`), handler names (repository: Create, Update, Toggle, Delete, RenumberOrder; social media: Toggle, Delete, TogglePlatform, DeletePlatform) and the old form fields (`active` and `version` on the repository lists, `enable` on the social media lists), so a page left open before the deployment still works.
- **Removed.** `_DocRepoListTable.cshtml`; `DocRepoListService.ListAsync` and `DeleteAsync` (and `DocRepoListItem`); the social media services' `GetSummariesAsync` and `DeleteAsync`. Their counting is now the shared rule. The library link on each count is gone; the library's own filters still find documents by type and office.
- The page header can now open a dialog (`AdminPageActionModel.ModalTarget`), and `admin-reference-edit-dialog.js` fills the shared list's Edit dialog from the row.
- `AdminNavigationCatalog`: the four entries no longer set `usesAdminShell: false`. The event types entry matches its whole folder (so New and Edit are in the shell too); the Platforms entry comes first so it wins for its own pages.
- No migration. Rollback: the previous release has the old pages; nothing stored changes.
- Tests: `AdminRepositoryAndSocialListsTests` (documents in the library and the trash and the import setting count; an unused value is deleted into the repository's audit with the typed order kept; an out-of-date xmin is refused; the policies; event types A to Z with description search and event counts; Guid delete refused and done with the old audit action names; each page renders the shared layout in the admin shell with its policy; New and Edit in the shell; old handlers and form fields). `DocRepoListPostgresIntegrationTests` (xmin through the shared list and a document filed while its type is deleted) and `AdminSearchEscapingPostgresTests` (an event logged while its platform is deleted) run on PostgreSQL in `database-migrations.yml`. `DocRepoListServiceTests`, `ListOrderTests`, `SocialMediaPlatformDeleteTests`, `AdminPageIndexTests` updated.
- Checked on screen at desktop and 375 px, light and dark, against the shared dev database (no migration): all four lists, the Add and Edit dialogs, the social media Add and Edit pages, no sideways scrolling.
