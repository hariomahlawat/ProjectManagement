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

### Merged pages

- **Restore deleted items** (on the Recovery centre route): tabs for Projects, Project
  documents, Repository documents, Calendar events and Chat files, each listing what can be
  restored and until when. Permanent deletion stays a separate confirmed step. Archived
  projects keep their own page because nothing is deleted.
- **Lists used in forms** (on the Master data route): one card per list with its value count
  and the form that uses it; the cards open today's list pages. Includes the Document
  Repository and social media lists.
- **Data jobs** (on the Maintenance route): PDF ingestion and legacy import with their last run.

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
