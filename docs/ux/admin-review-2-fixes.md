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
