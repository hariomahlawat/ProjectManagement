# Administration: findable, plain, low-friction

The aim, set on 6 Nov 2026: **an administrator can find every admin page and setting easily,
with less friction and text that explains itself.** This page records why the Administration
module is being reorganised, the approved design and the PR plan.

Approved mockup (clickable, example figures):
https://claude.ai/artifact/6tweBLinyxdcujXQW7Sjmr

## Why

A UI/UX review on 6 Nov 2026 (from the code and markup; no signed-in walkthrough yet) found:

- **The landing page tries to be four pages.** Header buttons, an "Operational oversight"
  banner, four metric cards, "Needs attention" beside "Recent activity", and 19 link tiles.
  The same destinations are linked up to four times, and nothing says "start here".
- **The status colours cry wolf.** The banner turns amber whenever any user must change a
  password (normal after creating a user) and red for any audit error in 24 hours; the
  Recovery card is amber whenever anything is in Trash ("Items awaiting recovery review").
- **The sidebar is long and mixes hubs with their pages.** 29 entries in 7 groups (Master Data
  alone has 11). Recovery centre, Master data centre and Maintenance centre are listed beside
  every page they link to. Sidebar names differ from page titles ("Master data centre" opens
  "Master data"; "Admin help" opens "Administration guide").
- **Groups follow the system, not the job.** Archived projects sit under Recovery; Legacy
  import under Maintenance points into another module; "Access governance", "Configuration
  integrity" and "Search index" do not explain themselves.
- **Admin pages are missing.** Document Repository admin (delete requests, document and office
  categories, OCR failures, missing files, its Trash), Media sources and Media Intelligence, and
  social media types and platforms are not in Administration at all. Two different document
  bins exist with no explanation.
- **The words are compliance language.** Eyebrows change on every page ("People and access",
  "Account lifecycle", "Configuration governance", "Master data · assurance"); descriptions
  say "concurrency-protected, audited run", "governed reference name", "controlled purge".
- **Help is a long policy text**, not answers to "how do I…".

## Rules

1. **Name tasks, not systems.** "Restore deleted items", not "Recovery centre". The sidebar
   name is the page title, word for word.
2. **Find anything by its everyday name.** One "Find a page or setting" box (Home and sidebar,
   `/` from anywhere) matches titles, descriptions and the words people use: "trash",
   "locked out", "dropdown", "holiday".
3. **Nothing hidden.** "Every admin page" lists every page with one sentence each.
4. **One place per job.** Each destination appears once in the sidebar and at most once on
   Home. No hub beside its own pages.
5. **Colour means something.** Amber or red only when an administrator has to act. A pending
   password change or items in Trash are normal and stay neutral.
6. **Plain words.** Every page opens with one sentence saying what it is for and whether its
   actions can be undone.

## Design

### Sidebar: 16 entries in 5 groups

| Group | Entry (= page title) | Today |
| --- | --- | --- |
| | Home | Overview |
| People | Users | Users |
| | Who has which role | Access governance |
| Sign-ins & audit | Sign-in activity | Login activity |
| | Audit log | Audit logs |
| | ERP usage | ERP usage |
| Restore & archive | Restore deleted items | Recovery centre, Project trash, Document recycle bin, Deleted events, chat restore |
| | Archived projects | Archived projects |
| Reference lists | Lists used in forms | Master data centre and its 9 lists, ARPP reference data |
| | Holidays & celebrations | Holidays, Celebrations |
| System | System health | System health, Search index (as a section) |
| | Data jobs | Maintenance centre, PDF ingestion, Legacy import |
| | Data checks | Configuration integrity |
| | Document repository | *(new)* delete requests, OCR failures, missing files |
| | Media library | *(new)* media sources, classification |
| | Chat file storage | Chat storage |
| (footer) | Every admin page · How do I… | Admin help |

**Shipped in PR 2:**
- The five groups and their task names. Page titles were renamed with them:
  - "Who has which role", "Sign-in activity" and "Audit log"
  - "Restore deleted items", "Lists used in forms", "Data jobs" and "Data checks"
  - "Chat file storage"
- The sidebar has 19 entries, down from 29, and every group starts open.
- Pages reached through a hub are out of the sidebar and highlight their hub instead. These are the bins, the individual lists, PDF ingestion and legacy import.
- Until the merged pages land, a few things stay separate:
  - Holidays and Celebrations are two sidebar entries.
  - Search index keeps its own entry.
  - The Document Repository appears as "Document deletion approvals", and media as "Media Sources".
- Document Repository and media pages keep their own module layout.
- The Administration sidebar is shown only to Admins. HoD reaching Holidays, Celebrations or Activity types keeps the ordinary navigation.

**Shipped in PR 3:**
- "Find a page or setting" in the sidebar matches each page's name, its search words and its
  one-line description. Every word typed must match ("public calendar" finds Holidays).
- Keyboard:
  - `/` on an Administration page jumps to the box. Ctrl+K still opens the app-wide search,
    and on phones, where the sidebar is hidden, `/` keeps opening the app-wide search.
  - Enter opens the first match.
  - The arrow keys move through the results and back to the box.
  - Escape clears the box.
  - Screen readers hear how many pages match.
- **Every admin page** (`/Admin/AllPages`) lists every page the viewer can open, grouped by task,
  with its sentence. It says where a page is also reached from ("Also reached from Lists used in
  forms") and when it opens in another module, and has the same filter.
- The sidebar, its search and the index use one access check (`IAdminNavigationAccess`), so they
  always show the same pages. The sidebar now has 20 entries (Every admin page joins Help).

### One catalogue drives everything

`AdminNavigationCatalog` becomes the single source for the sidebar, the search, the "Every
admin page" index, Home's task tiles and each page's title and one-line description. Each entry
gains a plain description and search words ("also found by"). A test keeps every page's title
equal to its catalogue name and every catalogue route resolvable.

### Home

1. "Find a page or setting" with a few example words.
2. One status line: "Running normally" unless something needs an administrator.
3. "To look at": only shown when something does (locked accounts, failed checks, purges due).
4. "What do you want to do?": six task tiles, each with one live figure.
5. "Recent changes by administrators", collapsed.

The 19-tile "Administrative areas" grid and the duplicated metric cards go.

**Shipped in PR 4** (`AdminHomeService`, rules in `AdminHomeRules`):
- **Find a page or setting** lists every page the viewer can open, best match first: a word
  in the page name counts most, then its search words, then its description. Example words
  ("locked out", "trash", "dropdown", "holiday", "pdf") fill the box, and a test keeps each one
  finding a page. `/` on Home goes to this box.
- **Status line.** Home never runs the system checks itself. It shows the last result
  (`IAdminSystemHealthService.GetLatest`, kept for a day), so opening Home never waits on
  storage or database checks.
  - Green "Running normally": the last checks passed.
  - Amber "Running, with warnings": a check has a warning.
  - Red "Needs attention": a check failed.
  - Grey "Not checked yet": the checks have not run since PRISM started.
  - The line also says how many things are listed to look at.
- **To look at** only lists what an administrator has to act on:
  - a failed system check (red) or a check with a warning;
  - locked accounts;
  - accounts waiting to be deleted;
  - errors in the audit log in the last 24 hours;
  - critical Data checks findings.

  Not listed, because they are normal: pending password changes, disabled accounts, items in
  the bins, audit warnings and failed sign-ins. These show as neutral figures on the tiles.
- **Six task tiles**, each with one live figure:
  - Add or manage users
  - Check sign-ins and the audit log
  - Restore something deleted (projects, project documents and calendar events: what its page
    can restore today; Document Repository trash joins with PR 5)
  - Edit the lists used in forms
  - Check that PRISM is healthy
  - Run a data job

  A tile appears only if the viewer can open its page. A figure that cannot be read leaves the
  tile without a number instead of breaking Home.
- **Recent changes by administrators** is folded away, with a link to the audit log.
- Purges that are due will join "To look at" with the Restore deleted items page (PR 5).

### Merged pages

- **Restore deleted items** (on the Recovery centre route): tabs for Projects, Project
  documents, Repository documents, Calendar events and Chat files, each listing what can be
  restored and until when. Permanent deletion stays a separate confirmed step. Archived
  projects keep their own page because nothing is deleted.
- **Lists used in forms** (on the Master data route): one card per list with its value count
  and the form that uses it; the cards open today's list pages. Includes the Document
  Repository and social media lists.
- **Data jobs** (on the Maintenance route): PDF ingestion and legacy import with their last run.

**Shipped in PR 5:** one tab strip (`RestoreTabs` view component, counts from
`IAdminRestoreTabsService`) over every bin.

| Tab | Page (address unchanged) |
| --- | --- |
| Overview | `/Admin/Recovery`: each kind, how many can come back, how long it is kept, and recent restores and permanent deletions |
| Projects | `/Admin/Projects/Trash` |
| Project documents | `/Admin/Documents/Recycle` |
| Repository documents | the Document Repository's Trash, which opens in that module |
| Calendar events | `/Admin/Calendar/Deleted` |
| Chat files | Chat file storage, at its undo list |

- Each tab keeps its own page, filters and confirmed restore and delete-for-good actions. Old
  links open the matching tab, and no handler or permission changed.
- The bin pages share the heading "Restore deleted items" and say in one sentence what restoring
  does and how long items are kept. In search they are "Deleted projects", "Deleted project
  documents", "Deleted calendar events" and "Deleted repository documents".
- A tab appears only if the viewer can open its page.
- Home's restore tile adds up the same tab counts, so Home and the page always agree.
- Projects past their retention are deleted automatically, so purges due are not a "To look at"
  item.
- Fixed in passing: deleted calendar events are hidden by a query filter, so they were counted
  as 0; the counts now lift that filter.

**Shipped in PR 6:** "Lists used in forms" (`/Admin/MasterData`) is one page of cards.
- The cards are grouped Projects, Activities and ARPP, Documents and social media, and
  Calendar.
- Each card shows the list's plain description, its value count (and how many are
  deactivated), and the form that uses it. Lists kept by another module say they open there.
- 13 lists, up from 9: Repository document types, Repository offices, Social media event types
  and Social media platforms join. The two social media lists are also in the catalogue now,
  so search and Every admin page find them.
- A card appears only if the viewer can open that list.
- The rule is said once: a value that records use cannot be deleted; deactivate it instead.
- The four total-record cards and the compliance wording ("Configuration governance",
  "Master-data domains") are gone. Recent changes show names, not user ids.
- Each list keeps its own page, route and permission.

**Shipped in PR 7 (System):**
- **System health includes search.** A "Search" group checks the search index. It is amber
  when the index is not ready or when items could not be added to search; until they are
  retried, those items are missing from results. The group links to the Search index page.
  Search index left the sidebar and highlights System health.
- Because the Search check is part of the system checks, Home's status line and "To look at"
  also show search failures. A signed-in review on 6 Nov found 27 failed indexing jobs (database
  error `42P08`, 23 to 25 Oct) that had not been shown anywhere outside the Search index page.
- Plain words on System health: "Some checks need a look", "Not run yet" (was "Starting /
  unavailable"), OK, Worth a look, Not working. Worker details no longer end in "..". The
  migration panels say "Database updates not yet applied" and "Database updates this version
  does not know".
- **Data jobs** names the two jobs by what they do ("Copy PDFs into the document repository",
  "Import projects from Excel"). The four "operating safeguards" cards became one sentence,
  and the unrelated "Restore deleted items" button is gone.
- **Data checks** names each check plainly ("Duplicate names", "Project type order") with one
  sentence each. Statuses are OK, Worth a look and Needs fixing. The repair confirmation says
  what happens: "Renumber this list 1, 2, 3… keeping its current order".
- Every route, policy and action is unchanged. The Search index, Data jobs and Data checks
  pages keep their handlers.

### Compatibility

- Every current URL keeps working: merged pages answer on their old routes or redirect to the
  matching tab (AGENTS.md: preserve routes).
- Pages from other modules (Document Repository admin, media, social media lists, Holidays,
  Celebrations, ERP usage) are linked from search, the index and the sidebar for administrators
  only. People who reach them from their own module (DocRepo managers, HoD for holidays) keep
  that module's navigation; whether a page shows the Administration sidebar depends on the
  viewer having `Admin.Access`.
- No permission changes: every entry keeps its current policy, and the sidebar and search show
  only what the viewer can open.
- Offline LAN: search is a small local script over a server-rendered list; no external assets.

## PR plan

| PR | Scope |
| --- | --- |
| 1 | This plan |
| 2 | One catalogue: five groups, task names, one-line descriptions and search words per entry; hubs out of the sidebar; Document Repository, media and social media admin pages in; Administration sidebar only for `Admin.Access`. Tests: every route resolves, policies unchanged |
| 3 | "Find a page or setting" in the sidebar (`/` shortcut, keyboard results) and the "Every admin page" index, both from the catalogue |
| 4 | Home: search, honest status line, "To look at", six task tiles, collapsed recent changes; the tile grid and duplicate cards removed. Tests for the status rules |
| 5 | Restore deleted items: one tabbed page on the Recovery centre route; old trash and bin URLs open the matching tab |
| 6 | Lists used in forms: one cards page on the Master data route, with counts and where each list is used |
| 7 | System: Data jobs (maintenance, PDF ingestion, legacy import), Data checks, System health with the search index section |
| 8 | Plain words: every admin page title and first sentence from the catalogue, one eyebrow per group, "How do I…" answers; the full guide stays linked |
| 9 | Phone and dark-theme pass, then a signed-in visual review with realistic data |

Each PR keeps current routes and permissions, ships with tests, and is merged when CI and the
Codex review are clean.
