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

## PR 3 as shipped

- **Sign-in activity (C3).** `AdminLoginMonitoringService` turns the Outcome filter and the review rule into the database query before the newest-5,000 cap. The review rule for a successful sign-in (between 22:00 and 05:00 IST) is `LateNightSuccessfulSignIn`, kept in step with `BuildEvent`; failed and locked-out attempts always need a look. The chart and the CSV export read the filtered set, and the "Sign-ins to look at" queue and its tile always read the review set, so an older failed sign-in is listed and counted even when newer successful ones fill the cap. "Too many to draw" now means a list on the page was cut off, and "Sign-ins in this period" shows the full count instead of the capped one. The usual sign-in times still come from the newest 5,000 events in the period.
- **Home (C7).** `AdminDashboardService` counts its 7 days as today and the six IST calendar days before it, the same period as Sign-in activity's 7 days. Recent changes show people through `AuditActorNames`, as the audit log does: full name, then user name, and "PRISM" for rows the application wrote. The account-state counts are left for PR 4.
- Tests: `AdminSignInCountsWhatItShowsTests` (filtered outcome and review rows older than the cap, export matches the list, the review rule translates to PostgreSQL, Home's IST window at a day boundary, Home's display names).

### PR 6 shipped: System health's jobs in plain words

- Every background job writes its last-run line through one helper (`Services/Admin/BackgroundJobMessages.cs`): "Nothing to remove", "Removed 3 items from the notebook bin", "Sign-in summary for 6 Nov is up to date". Dates read "6 Nov".
- "Login aggregation" is shown as "Sign-in summary". Its key `login-aggregation` is unchanged.
- A job turned off in the settings shows "Switched off", with no schedule and no warning. This applies to the Document Repository bin clean-up when `DocRepo:TrashRetentionDays` is 0, to audit and notification retention when they are off, and to chat attachment clean-up while attachments are off.
- Missing files shows its check time in IST with the zone. Its cache time is read from the check itself: 2 minutes, here and on System health. System health's own 45-second page cache is separate.
- Job status is kept in memory, so the old wording disappears when PRISM restarts with this version.

### PR 5 shipped: audit log names and figures

- **Actor column (U5).** The audit log and its details drawer show roles in full through `IAdminRoleDescriptorCatalog` ("Administrator, Head of Department, Project Officer" instead of "Admin,HoD,Project Officer"). People were already resolved through `AuditActorNames`, the lookup Home uses since PR 3: full name, then user name, and "PRISM" for rows the application wrote.
- **"Records changed" (C6).** The tile reads "8 people · 3 records changed". It counts distinct records, one per entity type and id, however many events name them; events that name no record are left out. The count runs in the database with plain string functions over the stored details.
- **Search (C6).** `AuditService` and `AdminAuditService` now store details without escaping letters (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`), so search finds "R&D", apostrophes and Hindi in new entries. The drawer's raw JSON shows older rows' letters as written too. The stored JSON is never rendered as markup: Razor encodes it, and the drawer embeds it through a second serialisation that still escapes `<`, `>`, `&` and `'`, and sets it with `textContent`.
- Older rows are not rewritten and still hold `\uXXXX` codes, so search can miss those letters in them. A short hint under the search box says so.
- No migration; routes, policies and the CSV export are unchanged.
- Tests: `AdminAuditLogNamesAndFiguresTests` (full role names and display names, the distinct-record count and its PostgreSQL translation, search for "R&D", an apostrophe and Hindi, the drawer JSON staying HTML-safe, older escaped rows reading as written).
