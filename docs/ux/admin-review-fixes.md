# Administration: cold review (6 Nov 2026) and fix plan

After the redesign in `admin-module.md` shipped, two independent reviewers looked at the
Administration module cold, without its design history:

- **UX:** every page in the running app, signed in as an Admin, at desktop, phone (375 px)
  and dark.
- **Code:** every admin page model, view, service, script and test on master, judged against
  `AGENTS.md`.

The main findings were then checked against the code.

**Verdict:** usability **6/10** for a first-time administrator; code health **8/10** (no
security hole found).

The redesigned core works and should be kept:
- Home, the sidebar search and Every admin page;
- Users, Restore deleted items and Lists used in forms;
- Data jobs, Data checks and "How do I…".

What is left falls into three kinds:
- problems that exist but are reported nowhere central;
- the outer pages that never got the redesign;
- a short list of code gaps.

## Findings

### High
1. **Real problems are not surfaced.**
   - Missing files: 110 of 115 repository files missing.
   - Media sources: 402 of 420 media unavailable.
   - Media intelligence: detector file missing.
   - Data checks: 21 problems.
   - None of these reach Home or System health, which reports Storage "All OK". Free space near
     the chat upload stop (26.9 GB free, uploads stop below 20 GB) is not flagged either.
2. **Broken stat tiles on Search index and Chat file storage.** Label, value and caption run
   together ("StatusReadyActive generation 19"), and they are clipped on phones. These pages
   use a bare `<div>` instead of the shared tile's `__copy` wrapper.
3. **The audit log does not say "who did what, to which record".**
   - All 25 rows on page 1 have the affected record "Not recorded".
   - One person appears four ways (full name, user name, "System", "PRISM system").
   - Some event titles are machine-made ("Arpp Ipa Stage Startup Reconciled"), with raw codes in
     the row.
4. **About ten linked pages leave the admin layout with no way back.**
   - These are the Document Repository, media and social media admin pages.
   - Their only back link is "Back to the library".
   - "Document deletion approvals" lands on a page titled "Decision Centre".

### Medium
5. **Names do not match.**
   - "Chat file storage" opens a page titled "Chat storage" (eyebrow "Recovery").
   - "Deleted repository documents" opens "Trash"; "Repository document types" opens
     "Document types".
   - "ARPP values" opens "ARPP reference data" ("Back to master data").
   - "Copy PDFs into the document repository" opens "PDF ingestion".
   - Legacy import's back button says "Maintenance centre".
   - Home says "Add a user", Users says "Create user".
   - The guide's body still uses the old page names.
6. **Role names differ by page.** Create user has "Administrator / Head of Department";
   Users and ERP usage have "Admin / HoD / Main_Office_Clerk"; Who has which role also lists a
   legacy "Main Office" role.
7. **Who has which role contradicts itself.**
   - The tile says "3 access findings"; the queue says "5 findings".
   - One finding is listed twice.
   - Normal states are amber: a single Commandant, an Admin who is also HoD.
   - Holder lists are cut off without "+N more" (8 of 15 shown).
8. **List order data has repeats.**
   - Every value has display order 1 in Project types, Sponsoring units and Line directorates,
     and 100 in the repository lists, so until someone moves a value the lists sort by name.
   - Moving works: the up/down arrows renumber the whole list
     (`AdminMasterDataCommandService.ReorderAsync`). The repeats come from how values are
     created or from older data.
   - Data checks finds the first three but not the repository lists, and its fix is on another
     page.
9. **Jargon on second-level pages.**
   - Search index: "Projection 7/7", "atomic generation activation", "Search V2 ranker".
   - Media sources: "projected durably", "Outbox", "dead-letter", raw config keys.
   - Legacy import ("idempotent") and PDF ingestion ("The coordinator").
   - Sign-in activity: "Materially abnormal sign-in time".
   - ERP usage: "Usage posture", "non-adoption signal".
   - A user's page: "Analytics classification".
   - Deleted projects tiles; raw `DocRepo:TrashRetentionDays` and "1 day(s)" in System health.
10. **The category tree can freeze the tab.** `admin-category-tree.js` follows children with no
    visited set. If a category sits inside itself, which Data checks can report, "Collapse"
    loops forever. The collapsed state is saved, so the page freezes on every later visit. The
    server also renders a two-node cycle twice.
11. **No guard against an admin page without a permission.** The Admin area convention only
    requires sign-in; each page's own `[Authorize(Policy)]` is the real gate, and no test
    checks that every page has one.
12. **"View all login activity" on a user's page is a dead end.** It opens the last 30 days,
    which can be empty while the user page lists earlier sign-ins.

### Low
- **Wrong figure on Home.** "86 values in use" is the number of active values; the lists
  record which values are in use separately.
- **Wrong time window.** Home's "errors in the last 24 hours" opens a 30-day audit view.
- **Not audited.** Search index Rebuild and Retry write nothing to the audit log.
- **"Run checks again"** answers its POST with a page, so F5 re-runs every check.
- **Unescaped searches.** Some admin searches treat `%` and `_` as wildcards (recovery bins,
  lists, Celebrations).
- **PDF ingestion failure CSV** uses its own escaper instead of `SafeCsv`.
- **ERP usage export** recomputes the whole dataset once per 100 rows.
- **Mixed and missing zones.** System health mixes IST and UTC times; Search index times have
  no zone; "18.6" is unlabelled.
- **Empty bins keep everything.** They still show four zero tiles and full filters, and say
  "match the current filters" when none are set.
- **Phone layout.**
  - Who has which role shows its tiles one per row.
  - The Users table scrolls sideways.
  - Media intelligence badges are cut off.
- **Wording.**
  - "1 sources".
  - "login" and "sign-in" are both used.
  - Social media lists say "Disable" where other lists say "Deactivate".
  - Celebrations has no "Lists used in forms" link.
- **Search shortcut.** `/` on Every admin page focuses the sidebar box, not the page's filter.

## Fix plan

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **Tiles and figures that are wrong.** Search index and Chat file storage use the shared stat tile; Home's lists figure says "values" (or shows the in-use count); the errors link opens the last 24 hours; "Run checks again" redirects after its POST; System health times in IST with zones, "PostgreSQL 18.6" labelled; "1 source(s)" plurals | H2, Low |
| 3 | **Report what is wrong.** New system checks: Document Repository files missing, media sources unavailable and the media model missing (only where the media library is enabled), free space against the chat upload stop. Each is amber with a sentence on what to do and a link, so Home's "To look at" shows it. Data checks warnings get a neutral figure on Home | H1 |
| 4 | **Audit log that answers who and what.** Actor shown by display name everywhere (one resolver); event titles from the presentation catalogue with a readable fallback; raw codes behind "Details"; "Affected record" column hidden when the event has none, instead of "Not recorded"; Search index Rebuild and Retry audited | H3, Low |
| 5 | **Outer pages lead back.** Document Repository, media and social media admin pages: an "Administration" back link for admins, titles that match the catalogue names; "Document deletion approvals" opens the repository deletion requests with a matching title | H4, M5 (part) |
| 6 | **Code guards.** Category tree: visited set in the script and the server so a cycle cannot loop or render twice, with a JS test; a reflection test that every admin page model has a policy (and matches its catalogue entry); `SearchLikePattern` in every admin search; `SafeCsv` for the PDF failure export; ERP usage export computed once | M10, M11, Low |
| 7 | **One name for each thing.** Page titles equal catalogue names (Chat file storage, ARPP values, Legacy project import back link, Create user); one role display-name map used on every page, legacy role hidden; "sign-in" everywhere; the guide's body text renamed | M5, M6, Low |
| 8 | **Who has which role tells the truth.** One findings count; no duplicates; single-holder and Admin+HoD neutral; "+N more" on holder lists; tiles two per row on phones | M7, Low |
| 9 | **List order data.** New values get the next number instead of a fixed 1 or 100 (moving already renumbers and is left as it is); repository lists included in Data checks, and their move behaviour checked; each list page shows its own order problem with the renumber button | M8 |
| 10 | **Plain words on the second-level pages.** Search index, Media sources, Media intelligence, Legacy import, PDF ingestion, Sign-in activity, ERP usage, the user page and the bin tiles; technical detail behind "Technical details"; the jargon test widened to these pages; "View all sign-ins" opens a period that includes the user's last sign-in | M9, M12 |
| 11 | **Phone and empty-state polish.** Users table without sideways scroll; badges that wrap; empty bins show one plain empty state; `/` focuses the page's own filter on Every admin page; social media lists say "Deactivate"; then a signed-in recheck of every page at desktop, phone and dark | Low |

### PR 7 as shipped

- **Page names.** A page shows its catalogue name; `AdminOneNameTests` checks every catalogue
  page's title, and the capability links on Who has which role. The lists on Lists used in forms
  are checked too. Choices made:
  - adding a user is "Add user" everywhere (Home, Users, the page itself and the guide);
  - PDF ingestion is "Copy PDFs into the repository", the name Data jobs already used;
  - ARPP reference data is "ARPP values";
  - the Document Repository's own pages and menus use the admin names: "Deleted repository
    documents", "Repository document types", "Repository offices" and "Document deletion
    approvals".
- **Role names.** Every page shows a role through `IAdminRoleDescriptorCatalog`: Users (badges,
  filter, chip), ERP usage and Who has which role. The old role names `Main Office` and
  `ProjectOffice` read "… (old role name)". They are left out of the Users filter and Who has
  which role while nobody holds them. Exports keep the stored role names.
- **What each role can do.** The admin jobs on Who has which role are named after their pages
  in plain sentence case ("Run data checks", "See who has which role").
- **Sign-in.** Visible text says "sign-in", not "login". Routes, export columns and stored audit
  actions are unchanged.
- **The guide.** Its body uses the current page names and plain words.

### PR 8 as shipped

- The "Need a look" tile and the list under it count the same things, each once. An account
  without a role is one "accounts without a role" line, not that line plus one per account. Each
  account keeps its own note in the access review export.
- Normal states are not findings: an Administrator who is also HoD is no longer flagged, and the
  single-holder tile is neutral (one Commandant is expected). One active Administrator is still
  flagged, because nobody else could recover the system.
- Holder lists end "and N more", linking to Users filtered by that role.
- The tiles sit two per row on phones.

Rules carried over from the redesign:
- every PR keeps routes, policies and data contracts;
- every PR ships with tests and is checked on screen while signed in;
- every PR is merged when CI and the Codex review are clean.

Out of scope here: running PDF ingestion as a background job instead of inside the request (a
hosting decision, noted by the code review).
