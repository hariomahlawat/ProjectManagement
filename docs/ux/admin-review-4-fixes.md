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

## Owner actions still open
- Mark Test HoD, Test Project Officer and the two Claude Review accounts as Test in Edit user → "Kind of account", so ERP usage stops counting them.
- Review the "Restored but still hidden" list next to Repository trash, and reactivate the documents that should be visible.
- Decide whether the System account (@system) should be marked as a Service account. PR 9 names it on Who has which role either way.
