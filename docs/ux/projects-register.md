# Projects repository (`/Projects`)

The repository lists every project anyone signed in may read. It is the page behind
**Projects → Repository**. This document describes what the page does today and the approved
plan to turn it into a register that shows which projects need attention.

## Where it lives

| Concern | Code |
| --- | --- |
| Page | `Pages/Projects/Index.cshtml(.cs)`; partials `_ProjectRepositoryHeaderSummary`, `_ProjectRepositoryLifecycle`, `_ProjectRepositoryResults`, `_ProjectRegisterActiveTable` (Active tab's table), `_ProjectRegisterPipeline` (Active tab's stage strip and quick filters), `_ProjectRegisterClosedTable` (Completed, Cancelled and All tables), `_ProjectRegisterQuickFilters` (quick-filter bar and chips), `_ProjectRepositoryLive` (live refresh), `_ProjectRepositoryLiveMetadata` |
| Active register | `Services/Projects/ProjectRegisterFactsService.cs` and `ProjectRegisterActiveFacts.cs` (facts and the attention order); `ProjectRegisterPipeline.cs` (stage strip counts, quick filters); `ViewModels/ProjectRegisterActiveRowVm.cs` and `ProjectRegisterClosedRowVm.cs` (row text) |
| Query | `Services/Projects/ProjectSearchFilters.cs` (`ApplyProjectSearch`, `ApplyProjectOrdering`) |
| Current position text | `ViewModels/ProjectRepositoryStagePositionVm.cs` over `ProjectLifecyclePositionResolver` |
| Script | `wwwroot/js/pages/projects-index.js` (live search and in-place links, More filters drawer, view switch) |
| Styles | `wwwroot/css/projects/index.css` |
| Tests | `ProjectIndexPageTests`, `ProjectRepositoryPresentationContractTests`, `ProjectSearchFiltersTests`, `ProjectRegisterFactsServiceTests`, `ProjectRegisterActiveRowVmTests` |

## Current behaviour

- **Lifecycle tabs**: Active, Completed, Cancelled, All, each with a count that follows the other
  filters. **Default tab** (owner decision, 27 Oct 2026): a plain `/Projects` (menu, breadcrumb,
  bookmark) opens the tab last chosen, kept in the `prism_projects_tab` cookie (HttpOnly, path
  `/Projects`, one year), or Active on a first visit. A link that carries any parameter keeps its
  meaning, All unless it names a tab, so Command desk and Analytics drill-downs are unchanged;
  such a link is not remembered unless it names a tab. Links name the All tab explicitly
  (`Lifecycle=All`) so choosing it is remembered too. **Legacy** is not a tab: the
  Completed tab has a record switch, All completed / Recorded in PRISM / Legacy entries
  (`Record=Prism|Legacy`), with a line saying legacy entries are part of Completed. Older links
  with `Lifecycle=Legacy` open Completed with Legacy entries selected. `Record` applies only on
  the Completed tab; the other tabs' links drop it.
- **Completed, Cancelled and All tables** (table view): Completed shows the project with case
  file and build, completion (day, else month and year, else year), category, Project Officer,
  ToT status and record source; Cancelled shows the stage where it ceased, the cancellation date,
  Project Officer and reason; All shows status, current position and Project Officer. Text comes
  from `ProjectRegisterClosedRowVm` and the resolved stage positions. Category, Project Officer,
  Status and Project headers sort the whole result.
- **Search** refreshes the results in place (`?handler=Live`) 300 ms after typing stops and keeps
  the URL current.
- **Quick-filter bar** (top of the results, refreshed with every live search): Project Officer,
  Category, Technical category and Build menus. Each option shows how many projects it would list
  under every other filter (the tab, search, pipeline and record filters included) but not the
  menu's own choice. Technical category lists the categories that have projects in scope, a
  sub-category labelled with its parent ("Simulators › Driving"); like the filter, each counts
  only projects tagged with that category itself. **More filters** opens the drawer with the
  rarer filters (HoD, project type, completion year, ToT, archived) and shows how many of them
  are set. Menu and chip links refresh the list in place.
- **Chips**: one per applied filter, built by the server, each with a remove link that drops only
  that filter (category also drops `IncludeCategoryDescendants`); **Clear all** keeps the tab.
  Filters that only arrive by link (Command desk and older Analytics links: the completion and
  stage-completion windows, `StageCode`, `StageCompletedMonth`, `SlipBucket`,
  `IncludeCategoryDescendants`) and the pipeline filters get chips too, so a narrowed list always
  says why. The Completed tab's record switch shows its own choice and has no chip.
- **More filters drawer**: changes apply only with **Show results**; closing it any other way puts
  its controls back to what they held when it opened, and closing it while Show results is loading
  cancels that request.
- **Cards or table**: the choice is kept in the `prism_projects_view` cookie (and local storage)
  and rendered by the server, so the chosen view appears on first paint without a flash.
- **Active tab, table view (the Active register)**: project with case file ("No case file" when
  none) and category; a stage tracker (one dot per workflow stage: done, skipped, current,
  blocked, waiting to start, upcoming) with the current stage and its state; time in stage with
  a late / days-left / No PDC badge and a bar against the stage's planned length; stage PDC and
  planned days; Project Officer; last update (orange after 30 days). Every value is a projection
  of the register facts below. An archived active project (shown with **Include archived**) has
  no facts and reads "Archived: timing not tracked". The card view and the other tabs' table are
  unchanged.
- **Ordering** is server-side over the whole filtered result before paging. On the Active tab
  the default is the attention order: blocked first, then late (most overdue first), then on plan
  (nearest PDC first), then no PDC (longest in stage first), then finished, ties by name. It is
  applied in memory to the filtered Active set before paging. Elsewhere the default (Operational)
  puts active projects first by latest remark or update, then completed by latest completion,
  then cancelled. A search orders by relevance first, and a column sort overrides both.
- **Active tab: where the projects stand** (above the results, cards and table): one column per
  workflow stage (the default workflow's order, then any other stage in use) with the number of
  active projects in it, split into on plan or no PDC, late against the stage PDC, and blocked;
  finished projects are in no column. Choosing a column lists only the projects now in that stage
  (`CurrentStage`; `StageCode` still means "has reached" for older links). Three quick filters,
  late against plan, blocked and not updated in 30+ days (`Attention`), combine with a stage. Each
  control's counts follow the other's choice and every other filter, but not its own choice. Both
  filters show as chips, count in the Filters badge, survive a live search, and apply only on the
  Active tab: the other tabs' links drop them. Their links are full page loads. The lifecycle tab
  counts ignore them; the results count shows the narrowed list.
- **Known order quirk**: in the Completed order, a project with only a completion year sorts
  above projects completed on a known day of that year (the database puts a missing month first
  in a descending sort). This predates the register.
- **Phone width**: tables scroll inside their box. `.projects-table-wrap` is positioned, so
  absolutely placed content (visually-hidden labels, progress-bar marks) cannot widen the page.

## Approved redesign: the register

The mockup was approved on 26 Oct 2026 (Design canvas "Projects register redesign"). Its
principles:

- The page answers "which projects need attention, and where is each one?", not just "which
  projects exist".
- Columns change with the tab. Active shows a stage tracker, current stage, **time in stage**
  against the stage plan, stage PDC, officer and last update. Completed shows completion
  month, ToT and record source. Cancelled shows the stage where it ceased, the date and the reason.
- Legacy entries are a filter inside Completed (All / Recorded in PRISM / Legacy entries), not a
  peer of the lifecycle tabs.
- Filters apply at once from a quick-filter bar; every applied filter is a removable chip.
- A quick-look panel opens a project's summary without leaving the list.
- Business facts are resolved once. Active rows reuse `OngoingProjectsReadService` (current stage,
  `CurrentStagePdc`, `DaysInCurrentStage`, `DaysPastPdc`), which already follows
  `StageStartInference` (a missing start is the day after the predecessors finished; the gap is
  the stage's own delay). The register, the quick look and the export must not recompute them.

### Delivery plan

Small PRs, each shippable on its own, with no database migrations. Every PR keeps the existing
query-string parameters working (`Lifecycle`, `Sort`, `Dir`, `p`, `PageSize` and all filters), so
bookmarks and links from the Command desk and Analytics keep working.

| # | PR | Scope | Status |
| --- | --- | --- | --- |
| 1 | Repository fixes | Drawer changes no longer leak into search (including a failed or still-loading apply); URL-only filters get chips; the view choice is rendered by the server; the Legacy pill says it is part of Completed; officer/HoD lists de-duplicated in the database; dead `TechnicalCategoryId` link parameter removed. | Merged (#2817) |
| 2 | Active register facts | `ProjectRegisterFactsService` / `ProjectRegisterActiveFacts` (below). Tests: late, on plan, waiting to start, blocked, no PDC, every stage done, last update, scope. No UI change. | Merged (#2818) |
| 3 | Active register table | New Active columns (project with case file and category, stage tracker, time in stage, stage PDC, officer, updated); default order blocked first, then most overdue; "No case file" replaces "Not recorded". Card view unchanged. | Merged (#2820) |
| 4 | Pipeline strip and attention filters | Stage columns with on-plan / late / blocked counts over the Active filters; clicking a stage sets the new `CurrentStage` parameter (not `StageCode`, which means "has reached"). Quick filters: late against plan, blocked, not updated in 30+ days (new `Attention` parameter). | Merged (#2821) |
| 5 | Lifecycle tabs and per-tab columns | Tabs Active, Completed, Cancelled, All; Legacy becomes a Completed sub-filter (`Lifecycle=Legacy` still opens Completed with legacy selected); Completed, Cancelled and All columns. | Merged (#2822) |
| 6 | Quick-filter bar | Inline Officer, HoD, Category, Build menus with counts replace the drawer for common filters; removable chips; **My projects**; rarer filters stay under **More**. | Merged (#2823) |
| 7 | Quick look | Side panel loaded from a page handler with the same authorization as the Overview read: current stage card, lifecycle with dates, people, latest remark, links. | Planned |
| 8 | Gallery | Cards become the Gallery view: cover photo, stage tracker and late/left badge for active projects, completion and ToT for completed ones. | Planned |
| 9 | Export | Excel export of the current filtered view through the resolved facts from PR 2; offline, no new dependency (ClosedXML is already used). | Planned |
| 10 | Header and phone | Compact header (one count line, search beside the title), `/` focuses search, phone rows stack as cards. | Planned |

### Active register facts (`Services/Projects/ProjectRegisterFactsService.cs`)

`GetActiveFactsAsync` returns one `ProjectRegisterActiveFacts` per active, unarchived,
undeleted project, keyed by project id. It reads `OngoingProjectsReadService` and adds only the
last update; archived active projects have no facts and show without timing. The page calls it
once per Active-tab request (including each live search refresh), which reads every active
project; that is fine at today's scale (about 60 active projects) and the place to look first if
the Active tab slows down.

| Fact | Source |
| --- | --- |
| `Stages` (tracker) | The workflow's stages in order, each Done, Skipped, Current, Blocked, Waiting (current but not started) or Upcoming |
| Current stage and `CurrentStageState` | The ongoing row's current stage: an in-progress stage wins, else the first stage not completed or skipped, else the last stage; In progress, Blocked, Not started, or Finished (every stage completed or skipped while the project is still open; the tracker keeps those stages Done or Skipped) |
| `DaysInStage` | The ongoing row's `DaysInCurrentStage`, decided by `PresentStageHelper.CurrentStageAgeDays` (also used by the PO workspace and Briefing desk): a stage that has begun, in progress or blocked, counts from its recorded start; otherwise from the latest completion |
| `StageAgeFrom` | Today minus `DaysInStage`, so it always agrees with the age |
| `StagePdc`, `DaysLate`, `DaysLeft` | The current stage's planned due date; `DaysLate` is the row's `DaysPastPdc` |
| `PlannedDays` | `StagePdc` minus `StageAgeFrom`, when both are known |
| `PlanStatus` | No PDC, On plan, or Late (PDC passed). A Finished project is never late or due: its last stage is done, so `DaysLate` and `DaysLeft` are empty and it sorts last |
| `LastUpdatedOn`, `DaysSinceUpdate`, `IsStale` | Latest of a non-deleted remark written or edited, a content edit, or creation (the activity the operational order uses), as an IST date; stale after 30 days |

Decided (27 Oct 2026): the default tab is the one last chosen, Active on a first visit. Later the
same day the owner replaced HoD and My projects in the quick-filter bar with Technical category:
HoD is rarely used to narrow the list, so it moved under More filters (`HodUserId` still works), and
My projects was removed (`Mine` is no longer read; it had shipped only in #2823 and nothing
linked to it).

### Deployment and rollback

All steps are presentation and read-model changes on existing data. Each PR rolls back by
reverting it; no data or configuration changes. The view cookie is a per-browser preference; if
it is absent the page shows cards, as before.
