# Administration: fourth cold review (8 Nov 2026) and fix plan

The earlier reviews and their fix PRs are in `admin-review-fixes.md`, `admin-review-2-fixes.md`
and `admin-review-3-fixes.md`. After the third plan and the owner's decisions shipped (#3004–#3019),
two independent reviewers looked at the module cold a fourth time:

- **UX:** all 43 pages on Every admin page, at 1440 px, signed in as an Admin; then a set at
  phone width (375 px) and in dark mode. The Add user, Edit user, Reset password and Disable account
  forms, and the Add pages and dialogs behind the shared lists, were opened without submitting.
- **Code:** the Administration code and the pages it links to, against `AGENTS.md`.

The main findings were checked against the code.

**Verdict:**

| Review | 1st | 2nd | 3rd | 4th |
| --- | --- | --- | --- | --- |
| Usability | 6 | 7 | 7 | **7.5** |
| Code health | 8 | 7.5 | 7.5 | **8** |

There were no High findings. The earlier fixes held, and no reviewer found a regression.

**Keep:**
- **Lists:**
  - one shared list page for all 11 reference lists, with tiles, one table and "In use, so deactivate it instead";
  - list delete: lock first, then count, then delete and audit, in one transaction.
- **Pages and wording:**
  - the media pages, the repository and social media lists, Repository trash and Restored but still hidden, all inside the admin layout;
  - figures that agree across Home, Recovery, Sign-in activity, the list hub and System health;
  - IST everywhere;
  - System health in plain words, with "Switched off" for the repository clean-up;
  - briefing decks in Restore deleted items.
- **Phones and dark mode:**
  - the active bin tab in view;
  - user rows and list rows as cards;
  - dark mode renders cleanly.
- **Code:**
  - the repository purge sets the file aside and commits before deleting it;
  - one policy registry;
  - service-level rechecks;
  - race tests in the PostgreSQL job.

**Not product defects:**
- the local data has missing repository files and photos;
- no face-recognition model files are installed;
- the app runs with the Development settings;
- all-equal list orders in the local data;
- test accounts counted in ERP usage, which is an owner action, already planned.

## Findings

### Code
- **C1 (Medium): two restores write their audit after the commit.**
  - Where:
    - the briefing deck restore (`ProjectBriefingDeckService.cs`, restore: `SaveChangesAsync`, then `AuditAsync`, which swallows failures);
    - the project document restore (`DocumentService.RestoreAsync`, which commits through `SaveAndQueueAsync`, then audits).
  - Both are reached from Restore deleted items.
  - **Document:** if the audit write fails, the document is restored with no audit record. The page says it could not be processed, and a retry then skips it.
  - **Deck:** a restore can go unaudited. Two admins restoring the same deck at once makes the second see "could not be restored", although the deck is back.
- **C2 (Medium): "Sort older images again" can un-claim a running job.**
  - `Pages/Admin/MediaIntelligence/Classifications.cshtml.cs` holds the workflow in a 922-line PageModel.
  - The running-job check is done before the transaction, with no lock.
  - Inside the transaction, the ClassifyMedia jobs are loaded without a status filter and reset to Pending with `LockedBy = null`.
  - A job a worker claimed in between can be run twice at once.
- **C3 (Low):** the reference-list usage count reads every `ProjectMetaChangeRequests` row in memory, 2–3 times per list request (`ReferenceListDefinition`). This source has no foreign key, so the row lock does not cover it.
- **C4 (Low):** in the repository purge, a database error on one document aborts the whole run (`DocRepoTrashPurgeService`), so the documents after it are never purged.
- **C5 (Low): the list convergence is unfinished.**
  - Project types, Sponsoring units and Line directorates still deactivate on a separate page, while the other lists switch on and off in place.
  - Their nine Create, Edit and Deactivate PageModels are near-duplicates that read the database directly.
- **C6 (Low): IST is formatted in three places by hand** (Chat storage's undo deadline, Missing files, Restored but still hidden), instead of through `IAdminTimeService.FormatIst`.
- Minor: in `PdfIngestionRunHistory.RecordAsync`, a failure in the trim step after the save reports that the summary could not be saved, although it was.

### UX
- **U1 (Medium-High): the Add and Edit forms behind the shared lists come in three designs.**
  - The lookups, categories and activity types use governance wording, for example:
    - "Create a controlled reference value for use in project records."
    - "Reference-list governance"
    - "Change control: Duplicate names and stale concurrent edits are rejected…"
  - The social media forms are plain. The Document Repository and ARPP lists use a dialog.
  - The buttons say "Create project type" in one place and "Add type" in another.
- **U2 (Medium):** the guidance box on 15 form pages (`admin-guidance-card`) has no styles. The Activity type checkbox label runs into its hint: "ActiveMake this type available…".
- **U3 (Medium): wide tables on phones.** On phones, Audit log, Sign-in activity, ERP usage and Media sources' unavailable items scroll sideways; who, when and the actions are off-screen.
- **U4 (Medium):** Unreadable PDFs and Missing files still leave the admin layout, with two competing ways back.
- **U5 (Medium): audit log entries are hard to read.**
  - The details and Affected record show internal text, for example "FfcGlobalFootprint additional slide enabled.", "project(s)" and "MasterDataIntegrity / sponsoring-unit-order".
  - Affected record is blank for many rows.
  - The Category filter says "Master data" and "PDF ingestion", while the sidebar says "Lists used in forms" and "Copy PDFs into the repository".
- **U6 (Medium):** Who has which role says "1 user account without a role" but does not name it. It is the System account. The Users role filter has no "No role" option.
- **U7 (Low-Medium):** the Document Repository lists say "equal numbers are listed A to Z", while Data checks flags every equal number as a problem.
- **U8 (Low):** the list hub still says "Opens in the Document Repository" or "Opens in Project office reports" for the four lists that now open in Administration.
- **U9 (Low):** three places still show user names instead of people's names:
  - the list hub's recent changes;
  - a user's page, audit history;
  - Sign-in activity's review row.
- **U10 (Low): formal wording is left in:**
  - Disable account and Add user;
  - the role capability list;
  - the bin dialogs: "Recovery operation", "Maximum 100 records per operation", and "Delete permanently" where the rest say "Delete for good";
  - Restored but still hidden;
  - ERP usage;
  - Celebrations.
- **U11 (Low): mismatches:**
  - a Sign-in activity tile caption that belongs to another figure;
  - Holidays' filter words differ from its column words;
  - System health says checks "have not run yet" while showing 0;
  - the main menu's Documents dropdown uses the old names "OCR failures", "Office categories" and "Document categories".
- **U12 (Low): phone polish:**
  - the Users and System health tiles are cut off;
  - Home's "To look at" rows wrap narrowly and repeat their text;
  - the bin tab strip's screen-reader labels widen the page to 986 px. It needs `position: relative`.
- **U13 (Low):** on Media sources, the table's sticky header covers the first row while scrolling.

## Fix plan

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **Restores are audited with the restore.** Deck and project-document restore write the audit entry in the same transaction (as Calendar recovery does), and a failed audit rolls the restore back; the admin deck restore requires the audit; a concurrent second restore reports "already restored"; tests with a failing audit and with two restores | C1 |
| 3 | **"Sort older images again" in a service.** The workflow moves out of the PageModel into a media service; jobs a worker holds are never reset (conditional update or row lock inside the transaction); the clock is injected; service-level tests including a job claimed mid-request | C2 |
| 4 | **Shared lists finished.** Usage from change requests read in SQL once per request; approving a change request whose proposed item was deleted is refused with a plain message; Project types, Sponsoring units and Line directorates switch on and off in place (old Deactivate URLs redirect); their Create and Edit pages collapse into one shared form; the Document Repository lists and Data checks agree on equal order numbers | C3, C5, U7 |
| 5 | **One Add and Edit form for every list, in plain words.** One form partial in the plain social-media style, "Add …" and "Save" everywhere; the governance wording removed; the guidance box styled (or removed) on all 15 pages; the Activity type checkbox label fixed | U1, U2 |
| 6 | **The last two pages in Administration.** Unreadable PDFs and Missing files render in the admin layout with one way back; the list hub drops the "Opens in …" hints for lists that now open in Administration; the main-menu Documents dropdown uses the admin names | U4, U8, U11 (part) |
| 7 | **A readable audit log.** Each action gets a plain detail and a display name for its record (project name, list name), with no "(s)"; Affected record filled where the record is known; Category names match the sidebar; people shown through the shared name lookup everywhere (list hub, a user's page, Sign-in activity) | U5, U9 |
| 8 | **Phones, round two.** Audit log, Sign-in activity, ERP usage and Media sources' unavailable items show rows as cards on phones, with who, when and the actions in view; tiles no longer cut off; Home's "To look at" rows read cleanly; the bin tab strip no longer widens the page; Media sources' sticky header no longer covers the first row | U3, U12, U13 |
| 9 | **Plain words, round four, and the last figures.** The role-less account named and linked on Who has which role, with a "No role" option in the Users filter; the remaining formal wording in account forms, role capabilities, the bin dialogs ("Delete for good" everywhere), Restored but still hidden, ERP usage and Celebrations; the Sign-in activity caption, the Holidays filter words and System health's "not run yet" sentence | U6, U10, U11 (rest) |
| 10 | **Small robustness.** The repository purge skips a document it cannot delete and carries on (logged, shown as failed); IST through `IAdminTimeService.FormatIst` in the three places; the PDF run summary's trim failure logged separately | C4, C6, minor |

**PR 10 shipped (small robustness):**
- **Repository purge:** a document whose row cannot be deleted is logged, its file is put back, and the run carries on with the rest (`DocRepoTrashPurgeService.RunExpiredPurgeAsync`). System health's last-run line says how many documents could not be removed and stay in the bin. The quarantine and concurrency steps are unchanged.
- **One IST formatter:** Chat storage (the undo deadline now ends in "IST"), Missing files and Restored but still hidden use `IAdminTimeService.FormatIst`. That formatter, and System health's job times, now write the day without a leading zero ("7 Nov 2026, 14:05 IST"), as those three pages already did.
- **PDF run summary:** a failure while removing old runs is logged as a trim failure, and no longer reported as "could not save".

**Order:**
- PRs 2, 3 and 10 touch separate code and can run in parallel with anything.
- PR 5 follows PR 4, because both change the list forms.
- PRs 6, 7 and 8 can run in parallel with each other.
- PR 9 goes last of the wording PRs, after PRs 5 and 7, because it touches some of the same pages.

**Rules carried over:**
- every PR keeps routes, policies and data contracts; old URLs keep working;
- every PR ships with tests and is checked on screen while signed in, at desktop and phone width, light and dark;
- every PR is merged when CI and the Codex review are clean.

No PR in this plan needs a database migration.

## PR 2 as shipped

**Shipped.** Both restores reached from Restore deleted items write their audit entry in the same transaction as the restore, as `CalendarRecoveryService.RestoreAsync` does. `AuditService.LogAsync` already saves through the caller's context and transaction, and puts the ERP-usage summary update in a savepoint, so it is reused as it is.

- **Briefing decks.** `ProjectBriefingDeckService.RestoreAsync` saves the deck and writes `ProjectBriefing.DeckRestored` inside one `RelationalTransactionScope`. The audit is required on this path, not best-effort: a failed write rolls the restore back and the error reaches the page. The owner's own restore in Briefing Studio goes through the same method and follows the same rule, so a deck never comes back without a record of who restored it on either path.
- **Two restores at once.** The second restore fails the deck's row-version check. Restore deleted items now says "Someone else restored this deck a moment ago. It is already back in Briefing Studio." instead of "could not be restored"; Studio says the same and opens the deck. Any other failure says "could not be restored, and nothing was changed".
- **Project documents.** `DocumentService.RestoreAsync` saves the document, queues its notification (still contained by the queue's savepoint) and writes `Project.DocumentRestored` in one transaction. A failed audit leaves the document in the recycle bin, so the bulk restore reports it as failed and a retry restores it instead of skipping it. The failed document is detached, so the next one in the batch does not save it again.
- Routes, handlers and policies are unchanged. No migration.
- Tests: `AdminDeckRestoreAuditTests` (audit in the same commit, a failing audit rolls back on the admin and Studio paths, two concurrent restores), and in `DocumentNotificationAtomicityTests` the document restore with its notification and audit, a failing audit, and the recycle-bin bulk path with a retry.

## PR 3 as shipped

**Shipped.** "Sort older images again" runs in `MediaReclassificationService` (Features/MediaLibrary/Admin); the Classification review handler only calls it and shows the result.
- Everything happens in one transaction. The service picks the batch (at most 500 older images not already waiting), then locks every processing job of those images (`SELECT … FOR UPDATE`, ordered by Id) before deciding anything.
- An image whose sorting job (AnalyseAsset, ReclassifyAsset, ClassifyMedia or RebuildIntelligence) is held by a worker with a live lease is left alone and counted; the page says "… already in progress were left alone." A worker claiming with `FOR UPDATE SKIP LOCKED` either finished its claim before the lock (the image is skipped) or skips the locked rows until the commit. A job whose lease ran out is not held, as in the worker's own recovery.
- The worker rotates `ClassificationConcurrencyToken` when it finishes. If an image changes between reading and saving anyway (a reviewer, the scanner), nothing is saved and the page says "Some older images changed while this ran, so nothing was queued. Try again." instead of failing.
- The clock is `IClock`. The per-image classification audits commit with the change, and the admin audit entry and the face-retirement wording are unchanged. The stale and reviewable rules are defined once in the service and used by the page's filter and counts.
- Tests: `MediaReclassificationServiceTests` (queueing, held and expired jobs, the skipped count, all held, already waiting, batches, audits, a failed admin audit) and `MediaReclassificationPostgresIntegrationTests` (a worker claims a job just before the lock and keeps it; during the lock a `SKIP LOCKED` claim cannot take a job being reset), which runs in `database-migrations.yml`.

### PR 4 shipped: shared lists finished

**Shipped.** C3, C5 and U7.

- **Change requests counted in the database (C3).** On PostgreSQL the change-request usage source is one SQL query, like the other sources: for each request it reads the item it started from, the proposed technical category column where there is one, and the id inside the JSON payload, and counts the request once per item. The payload is read with a pattern (`"projectTypeId": 12`), not a cast to jsonb, so one malformed payload cannot break the list page; a value that is not a plain number, or is too big, is skipped, as the old reader skipped it. On the test databases the requests are still loaded and parsed, now once per page instead of up to three times (tiles, In use filter, Used by). `AdminSearchEscapingPostgresTests` checks that the SQL count matches the old in-memory reader for all five lists that count change requests, including skipped payloads.
- **Approving a request whose proposed item was deleted (C3).** The decision service now tells a deleted item from an inactive one: "The project type this request proposes has been deleted, so it cannot be approved. Reject it and ask for a new request." (likewise for sponsoring units, line directorates, categories and technical categories). Before, it said "no longer active", or failed on the project's foreign key if the item went between the check and the save; that race now returns a plain message too ("A value this request proposes has just been deleted…") and nothing is saved.
- **The three lookups switch in place (C5).** Project types, Sponsoring units and Line directorates use the shared list's Toggle handler with the state asked for and the row version shown, through the existing `Set*ActiveAsync` commands, like the other lists. A form without a state is refused, never flipped.
- **Old Deactivate URLs.** `/Admin/Lookups/{list}/Deactivate?id=…` now redirects to the list. A Deactivate form left open from before the change still does what it said, with the version it showed, then returns to the list.
- **One Add and Edit form.** The nine Create, Edit and Deactivate PageModels, which read the database directly, are gone. Each folder keeps its `Create.cshtml`, `Edit.cshtml` (`{id:int}`) and `Deactivate.cshtml`, so routes and page names are unchanged, but they share `LookupItemModel` and `LookupDeactivateModel` (`Areas/Admin/Pages/Lookups/LookupPages.cs`), which pick the list from the page's folder. Edit reads the item through `IReferenceListService`; saving goes through the master-data commands as before. The form (`_LookupForm.cshtml`) is plain: a name, Cancel, and "Add project type" or "Save". PR 5 styles every list form together. The old form field names (`Input.Name`, `Input.SortOrder`, `Input.RowVersion`) still bind.
- **Order rule (U7): Data checks follow the repository lists.** The Document Repository lists are ordered by the number people type (-999 to 9999): lower first, equal numbers A to Z, and a delete leaves the rest alone. Their pages say so, so the Document type order and Office order checks no longer flag equal or spaced numbers, and the list pages no longer say "The order of this list has gaps or repeats." The checks' descriptions state the rule. This was the smaller and safer of the two ways: making the repository lists 1, 2, 3 would change how people enter the order and leave today's data flagged until someone renumbered it. No data is rewritten. The admin lists kept 1, 2, 3 (project types, sponsoring units, line directorates, categories) still flag gaps and repeats. Renumbering a repository list is still possible through the existing RenumberOrder handler, but Data checks no longer offers it there.
- No migration. Routes, handlers and policies unchanged. Rollback: the previous release has the old pages; nothing stored changes.
- Tests: `AdminSharedListsFinishedTests` (change requests read once per page; the in-place switch on each lookup, an old version and a missing state refused; the old Deactivate URL redirects and its old form still works; the one form adds, renames, refuses a duplicate name and 404s a missing item on each lookup; the pages keep their files and routes and share one model with the list policy). `ProjectMetaChangeDecisionServiceTests` (a deleted proposed item refused plainly, for all five lists, with nothing saved or audited). `ListOrderTests` (equal and spaced repository numbers accepted by Data checks and the list notice; project types still flagged). `AdminSearchEscapingPostgresTests` (the SQL count matches the in-memory reader).

### PR 5 shipped: one plain form for every list

**Shipped.** U1 and U2.

- **One form.** Every list's Add and Edit page goes through one partial, `Areas/Admin/Pages/Shared/_AdminListForm.cshtml`, built by `AdminListFormModel.For(list, …)`. It writes the heading ("Add project type", "Edit project type"), one plain sentence on what the list is for and where it is offered, a Back link, Cancel, and the button: "Add project type" on Add, "Save" on Edit. The page passes only its fields, so each page keeps its own handler, form field names and validation.
- **The words in one place.** `AdminListFormWords` holds the sentence for each of the 13 lists (for example "The kind of project, offered in the project form.") and the shared hints: what a rename changes ("Records that already use it show the new name."; for ARPP values, "ARPP entries already saved keep the wording they were saved with."), the Active switch ("Offered in new forms. Records that already use it keep it either way.") and the description ("Optional. Helps people pick the right one."). The numbered "01" card headers, the eyebrows, the guidance asides and the governance wording ("controlled reference value", "Change control", "Taxonomy guidance" and so on) are gone from the list forms. The list header buttons say "Add project category" and "Add technical category" instead of "Add category".
- **Pages, not dialogs.** Every list now adds and edits on a page of its own. 8 of the 11 list pages already did; the Document Repository lists and the ARPP values used a dialog. Pages were chosen because they need no script to fill the fields, keep their own address (Back works, a link can be shared), show what was wrong on the same page without reopening a dialog, and fit a phone. The new pages:
  - `/DocumentRepository/Admin/DocumentCategories/Create` and `/Edit/{id}`, and the same under `OfficeCategories`, share `DocRepoListItemModel`, which picks the list from the folder and saves through `IDocRepoListService` (same permission check, duplicate and version checks and repository audit as the dialog).
  - `/Admin/MasterData/ArppReferences/Create?list=…` and `/Edit/{id}?list=…` use `ArppReferenceItemModel`, which saves through `IArppReferenceDataService.SaveAsync` as the dialog did. The list and id come from the address, not the form. Only DFPDS schedules show a description, as before.
  - The list's Edit link keeps the list's route values (`?list=` on the ARPP values). The two dialogs, their scripts (`admin-reference-edit-dialog.js`, `arpp-reference-data.js`) and the shared list's `EditDialog` option are removed.
  - The old dialog handlers stay for a form opened before the change: the repository lists' `Create` and `Update`, and the ARPP `Save`. A failed old `Create` or `Save` now returns to the list with the message (a field error included), since the dialog that showed it is gone.
- **The guidance box (U2).** `.admin-guidance-card`, `.admin-switch-row`, `.admin-form-layout` and `.admin-form-grid--two` are styled with the admin tokens, so dark mode follows. The box stays only where it adds something the form does not say: Holidays (Add and Edit) and Celebrations (Edit), reworded plainly ("A Restricted Holiday shows on the calendar, but the office stays open."). The other six pages that had one are list forms, where it only repeated the form or was governance wording, so it was removed there. The Active switch now has its label and hint on two lines with space beside the box: "Active" / "Offered in new forms…".
- Routes kept, two pairs of pages added. Handlers, field names, validation and policies unchanged. No migration. Rollback: the previous release has the dialogs; nothing stored changes.
- Tests: `AdminListFormsTests` (all 13 lists covered; each list's Add and Edit page uses the shared form; no list page opens a dialog; one plain sentence per list; "Add …" and "Save"; header buttons; the switch spacing; the repository pages post to the list of their folder with the version shown, refuse a missing order, keep a duplicate name on the page and return to the list when out of date; the ARPP pages save into the list of the address and map field errors; policies). `AdminPlainWordsTests` (the old phrases stay out of every list form, and every guidance box left is styled and plain).

### PR 6 shipped: the last two pages in Administration

**Shipped.** U4, U8 and part of U11.

- **Unreadable PDFs and Missing files in the admin layout (U4).** Both pages (`/DocumentRepository/Admin/OCRFailures/OcrFailures`, `/DocumentRepository/Admin/MissingFiles`) render inside Administration for administrators: sidebar with System health lit, mobile header, the "System" eyebrow and the shared admin page header, like the Repository trash (third review, PR 14). "Read all again" moved into the header's actions and shows while anything is listed. The "← Administration" strip hides itself, and "Back to the library" now shows only to a Head of Department, who can open the pages but gets the normal layout; so each viewer has one way back. The Documents tab in the top bar is no longer lit there.
  - Their catalogue entries drop `usesAdminShell: false` and name System health as the sidebar parent, so Every admin page says they open from System health. Missing files' icon was a Bootstrap icon that does not exist; it is now `bi-file-earmark-x`, as in the library's menu. System health's Missing files sentence no longer says "(Document Repository)".
  - Inside the shell the library body drops its own gutter (`.admin-shell__content .library.docrepo-admin-page`), as the trash does.
- **"Opens in …" only where it is true (U8).** `/Admin/MasterData` asks the admin catalogue whether a list's page renders in Administration, and names another module only when it does not. Repository document types, Repository offices and both social media lists lose the hint. Every admin page no longer names the Document Repository for any page; only Document deletion approvals still says "Opens in the Decision Centre".
- **Main menu names (part of U11).** The Documents dropdown says Unreadable PDFs, Repository offices and Repository document types, the names Administration uses.
- Routes, handlers and policies unchanged; old URLs work. No migration. Rollback: the previous release renders the pages in the library's layout.
- Tests: `AdminLastPagesInShellTests` (both pages in the shell under System health, the admin header and one way back, the hint only for a list that leaves Administration); `AdminPageIndexTests`, `AdminNavigationCatalogTests` and `RoleBasedNavigationProviderTests` updated.

## Owner actions still open
- Mark Test HoD, Test Project Officer and the two Claude Review accounts as Test in Edit user → "Kind of account", so ERP usage stops counting them.
- Review the "Restored but still hidden" list next to Repository trash, and reactivate the documents that should be visible.
- Decide whether the System account (@system) should be marked as a Service account. PR 9 names it on Who has which role either way.
