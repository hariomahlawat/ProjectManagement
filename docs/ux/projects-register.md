# Projects repository (`/Projects`)

The repository lists every project anyone signed in may read. It is the page behind
**Projects → Repository**. This document describes what the page does today and the approved
plan to turn it into a register that shows which projects need attention.

## Where it lives

| Concern | Code |
| --- | --- |
| Page | `Pages/Projects/Index.cshtml(.cs)`; partials `_ProjectRepositoryHeaderSummary`, `_ProjectRepositoryLifecycle`, `_ProjectRepositoryResults`, `_ProjectRepositoryLive` (live refresh), `_ProjectRepositoryLiveMetadata` |
| Query | `Services/Projects/ProjectSearchFilters.cs` (`ApplyProjectSearch`, `ApplyProjectOrdering`) |
| Current position text | `ViewModels/ProjectRepositoryStagePositionVm.cs` over `ProjectLifecyclePositionResolver` |
| Script | `wwwroot/js/pages/projects-index.js` (live search, filter drawer, chips, view switch) |
| Styles | `wwwroot/css/projects/index.css` |
| Tests | `ProjectIndexPageTests`, `ProjectRepositoryPresentationContractTests`, `ProjectSearchFiltersTests` |

## Current behaviour

- **Lifecycle tabs**: All, Active, Completed, Cancelled, each with a count that follows the other
  filters. **Legacy archive** sits beside them; legacy projects are a subset of Completed, and
  the pill says so to screen readers and on hover.
- **Search** refreshes the results in place (`?handler=Live`) 300 ms after typing stops and keeps
  the URL current.
- **Filters drawer** (category, technical category, build, project type, officer, HoD, completion
  year, ToT, archived). Changes apply only with **Show results**; closing the drawer any other way
  puts its controls back to the filters in the URL, so a later search never picks up an unapplied
  choice.
- **Filters that only arrive by URL** (Command desk and older Analytics links): the completion and
  stage-completion windows, `StageCode`, `StageCompletedMonth`, `SlipBucket` and
  `IncludeCategoryDescendants`. Each shows a chip and counts toward the Filters badge, so a
  narrowed list always says why, and **Clear filters** removes them.
- **Cards or table**: the choice is kept in the `prism_projects_view` cookie (and local storage)
  and rendered by the server, so the chosen view appears on first paint without a flash.
- **Ordering** is server-side over the whole filtered result before paging. The default
  (Operational) puts active projects first by latest remark or update, then completed by latest
  completion, then cancelled; a search orders by relevance first.

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
- Filters apply at once from a quick-filter bar; every applied filter is a removable chip;
  **My projects** is one switch.
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
| 2 | Active register facts | `ProjectRegisterFactsService` / `ProjectRegisterActiveFacts` (below). Tests: late, on plan, waiting to start, blocked, no PDC, last update, scope. No UI change. | In review |
| 3 | Active register table | New Active columns (project with case file and category, stage tracker, time in stage, stage PDC, officer, updated); default order blocked first, then most overdue; "No case file" replaces "Not recorded". Card view unchanged. | Planned |
| 4 | Pipeline strip and attention filters | Stage columns with on-plan / late / blocked counts over the Active filters; clicking a stage sets `StageCode`. Quick filters: late against plan, blocked, not updated in 30+ days (new `Attention` parameter). | Planned |
| 5 | Lifecycle tabs and per-tab columns | Tabs Active, Completed, Cancelled, All; Legacy becomes a Completed sub-filter (`Lifecycle=Legacy` still opens Completed with legacy selected); Completed, Cancelled and All columns. | Planned |
| 6 | Quick-filter bar | Inline Officer, HoD, Category, Build menus with counts replace the drawer for common filters; removable chips; **My projects**; rarer filters stay under **More**. | Planned |
| 7 | Quick look | Side panel loaded from a page handler with the same authorization as the Overview read: current stage card, lifecycle with dates, people, latest remark, links. | Planned |
| 8 | Gallery | Cards become the Gallery view: cover photo, stage tracker and late/left badge for active projects, completion and ToT for completed ones. | Planned |
| 9 | Export | Excel export of the current filtered view through the resolved facts from PR 2; offline, no new dependency (ClosedXML is already used). | Planned |
| 10 | Header and phone | Compact header (one count line, search beside the title), `/` focuses search, phone rows stack as cards. | Planned |

### Active register facts (`Services/Projects/ProjectRegisterFactsService.cs`)

`GetActiveFactsAsync` returns one `ProjectRegisterActiveFacts` per active, unarchived,
undeleted project, keyed by project id. It reads `OngoingProjectsReadService` and adds only the
last update; archived active projects have no facts and show without timing.

| Fact | Source |
| --- | --- |
| `Stages` (tracker) | The workflow's stages in order, each Done, Skipped, Current, Blocked, Waiting (current but not started) or Upcoming |
| Current stage and `CurrentStageState` | The ongoing row's current stage: an in-progress stage wins, else the first stage not completed or skipped, else the last stage; In progress, Blocked, Not started, or Finished (every stage completed or skipped while the project is still open; the tracker keeps those stages Done or Skipped) |
| `DaysInStage` | The ongoing row's `DaysInCurrentStage` (`PresentStageHelper`) |
| `StageAgeFrom` | Today minus `DaysInStage`, so it always agrees with the age |
| `StagePdc`, `DaysLate`, `DaysLeft` | The current stage's planned due date; `DaysLate` is the row's `DaysPastPdc` |
| `PlannedDays` | `StagePdc` minus `StageAgeFrom`, when both are known |
| `PlanStatus` | No PDC, On plan, or Late (PDC passed) |
| `LastUpdatedOn`, `DaysSinceUpdate`, `IsStale` | Latest of a non-deleted remark written or edited, a content edit, or creation (the activity the operational order uses), as an IST date; stale after 30 days |

Known gap to decide (outside this plan): for a **blocked** stage with a recorded start,
`PresentStageHelper` counts the age from the latest completion, not from that start, so it can
read far longer than the stage has existed. The same number shows on the Ongoing page, the
Briefing desk, the officer conference and Analytics; the register uses it as is until the shared
helper is corrected.

Open decision for the owner: which tab opens by default. Recommended: remember each user's last
tab, with Active as the first-visit default; until decided, All stays the default.

### Deployment and rollback

All steps are presentation and read-model changes on existing data. Each PR rolls back by
reverting it; no data or configuration changes. The view cookie is a per-browser preference; if
it is absent the page shows cards, as before.
