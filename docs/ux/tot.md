# Transfer of Technology pages

Transfer of Technology (ToT) records, for each completed project, whether its technology has been
transferred to production: status, start and completion dates (year, month or day), MET and the
first production model. This document describes the pages today and the approved plan to turn
them into one page that serves the HoD, the Comdt and Project Office. The data rules (approval,
history, who may submit) are in [data-domain.md](../data-domain.md#transfer-of-technology-tracker-modelsprojecttotcs).

## Where it lives

| Concern | Code |
| --- | --- |
| Summary page | `Areas/ProjectOfficeReports/Pages/Tot/Summary.cshtml(.cs)` (counts, sentence, lists; `?handler=Yearly` for the chart) |
| Tracker page | `Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml(.cs)`; partials `_TotFiltersModal`, `_SubmitTotUpdateModal`, `_LatestTotRequestModal` |
| Project page card and drawer | `Pages/Shared/Components/ProjectTotCommandCard`, `Pages/Projects/_ProjectTotDrawer.cshtml`, `Pages/Projects/Overview.Tot.cs` (`TotDetails`, `Tot`, `TotRemark` handlers) |
| Shared form fields | `Pages/Shared/Tot/_TotFormFields.cshtml` (tracker submit), `wwwroot/js/projects/tot-form-fields.js` |
| Reads and writes | `ProjectTotTrackerReadService` (rows, history), `ProjectTotService` (save, submit, decide), `ProjectTotPresentation` (labels, card, date entry), `ProjectTotExportService` + `ProjectTotExcelWorkbookBuilder` |
| Scripts and styles | `wwwroot/js/projects/overview-tot.js`, `wwwroot/js/pages/project-office-reports/tot.js`, `tot-chart.js`; `wwwroot/css/tot.css`, `wwwroot/css/pages/project-tot-drawer.css` |
| Links in | Projects menu and dashboard signal (`OpsSignalsService` links the Summary); Command desk completion drill-down (tracker with `CompletedFrom`/`CompletedTo`); Approvals → Pending (ToT requests) |

Anyone signed in may view; Admin, HoD, Project Office (and Alternate) submit for any project and a
Project Officer for projects they lead; Admin and HoD approve and save directly.

## What the review found (Nov 2026)

Reviewed on the local data (139 projects) at 1536 px and on a phone.

- The module reads as three tools: a Summary, a tracker and the project drawer, with four names
  for itself and three verbs for one action.
- The tracker is laid out as a work queue but holds mostly empty data: a start date on 1 project
  of 139, a MET date on none, a request on one. Half its columns are dashes and every row says
  "No request".
- The HoD approves without seeing what changes: the proposed values are in a separate pop-up that
  repeats the request state six times, never sets current against proposed, and does not show a
  rejection's reason.
- Nothing puts first what needs doing; nothing sorts; each row click reloads the page; on a phone
  the table becomes one unlabeled column and the details start about 1000 px down.
- Small text (10 px headers, chips and labels); three date styles; duplicate filter buttons; the
  search box disappears when a search finds nothing.
- The real backlog is old: 53 projects have not started ToT, 10 of them completed more than ten
  years ago.

Fixed already: Summary total counted Not-required projects; the yearly chart skipped empty years;
dates saved before precision was tracked showed blank (#2883).

## Approved redesign

Mockup: Design canvas "ToT redesign mockup" (5 Nov 2026): the HoD page, the drawer's decision,
the phone view, and the Comdt and Project Office versions.

- **One page**, "Transfer of Technology", in place of Summary plus tracker: Position, a
  role-specific top section, then the Register. `/ProjectOfficeReports/Tot/Summary` keeps working
  and opens the page.
- **Position**: counts that filter the register (Need ToT, Completed with %, In progress, Not
  started; Not required counted apart), a completed / not-started bar, and the per-year chart.
- **The top section follows the reader's role** (who opens it: HoD and Comdt to check, Project
  Office to maintain):
  - HoD and Admin: *Waiting for your decision*, each request showing its change
    ("In progress → Completed, Oct 2026"), submitter and age; then *Not started, over five years*.
  - Comdt (and any viewer who cannot submit): *Recently completed* and *Not started, over five
    years* (read-only). Nothing to act on.
  - Project Office: *Not started, over five years*, each with Update (record a start, propose Not
    required, or propose **Not known**), and *Sent for approval* with "Waiting for HoD since …".
- **Optional details are never prompted for** (owner decision, 5 Nov 2026): record whatever is
  known. A year-only date, no start date, no MET and no production model are normal, not gaps,
  and some do not apply to every project. Start date and MET stay as optional fields.
- **Not known** (owner decision, 5 Nov 2026): a fifth status for a project where nobody knows
  whether ToT happened, typically one completed long ago. It is a real answer: it leaves the
  long-waiting list, counts as applicable but not completed, and shows as its own count and filter.
  Like any status it goes to the HoD for approval when Project Office proposes it.
- **Long waiting** means *Not started* with the project completed more than five years ago (23 in
  the local data).
- **Register**: inline search, status buttons with counts instead of the filter pop-up, columns
  that appear only when some listed project has data, a Note column ("3 years after completion",
  "Waiting 21 years", "Year only"), sorting. Readable sizes (13 px minimum).
- **One drawer** (the project page's) opens from a register row too, replacing the tracker's
  detail panel and both pop-ups. It shows the current position, history, and for a pending
  request a Now → Proposed table of only the changed fields with Approve / Reject (approvers) or
  "Waiting for HoD since …" (submitter). Its buttons follow the same rules as today.
- **Phone**: two counts, a "waiting for your decision" bar, search, scrolling status buttons, one
  card per project.
- **No withdraw** (owner decision, 5 Nov 2026): a request sent by mistake is rejected by the HoD
  with a reason, which keeps the history honest.
- Business facts are resolved once (`ProjectTotRegisterFacts`, PR 2) and reused by the page, the
  drawer, the export and later the Command desk.

### Delivery plan

Small PRs, each shippable on its own, no database migration. Existing addresses and parameters
keep working: `/Tot`, `/Tot/Summary`, `SelectedProjectId`, the filter parameters,
`CompletedFrom`/`CompletedTo` from the Command desk, `/Projects/Tot/Edit/{id}` and the drawer's
`?tot=open|edit`.

| # | PR | Scope | Status |
| --- | --- | --- | --- |
| 1 | Words, dates, sizes | One name ("Transfer of Technology") and one verb per action; one date style everywhere, MET and manufacture dates included; breadcrumbs that agree; one Filters entry; the search box stays when nothing matches; no "No request" on every row; 13 px minimum. | Merged (#2884) |
| 2 | Register facts | `ProjectTotRegisterFacts` per project: status, dates at precision, project completion year, years waiting, long-waiting flag, the pending request as a list of changed fields, the latest decision; plus the Position counts. Tests. No UI change. | Merged (#2885) |
| 3 | Not known: read everywhere | `ProjectTotStatus.NotKnown` (stored as text "NotKnown"; the column is text, max 32, no check constraint, so no migration). Every reader handles it: labels, Position counts, filters, export, the Summary and chart, Command desk and completed-portfolio counts, Approvals → Pending, applicability rules. Nothing offers it yet, and `ProjectTotService` refuses it (`ProjectTotPresentation.IsRecordable`: only `StatusOptions` may be saved), so no row can hold it. | Merged (#2887) |
| 4 | Not known: can be recorded | The form and the register's status buttons offer Not known. Ships only after PR 3 is deployed everywhere, because code before PR 3 cannot read the value. The drawer's status list now comes from `StatusOptions` too. | In review |
| 5 | Drawer: decide in place | The drawer shows a pending request as Now → Proposed (changed fields only, rejection reason, history) with Approve / Reject for approvers through `ProjectTotService.DecideRequestAsync` (version token and reason rules unchanged), and "Waiting for HoD since …" for others. Project page only. | Planned |
| 6 | Register opens the drawer | Tracker rows open the shared drawer in place; the detail panel and the request, submit and filter pop-ups go. Approvals → Pending keeps working. | Planned |
| 7 | One page | Summary's Position and chart join the page; `/Tot/Summary` opens it; menu and dashboard links follow. | Planned |
| 8 | Role top section | HoD, Comdt and Project Office sections from PR 2's facts; the Command desk links here. | Planned |
| 9 | Register table | Status buttons with counts, live search, data-driven columns, Note column, sort; filters from links get removable chips. Export follows the visible register. | Planned |
| 10 | Phone and clean-up | Phone cards and summary bar; unused rules removed from `tot.css`. | Planned |

### Register facts (`Areas/ProjectOfficeReports/Application/ProjectTotRegisterFacts.cs`)

`ProjectTotRegisterFactsService.GetAsync(filter)` reads the tracker rows once
(`ProjectTotTrackerReadService`) and `ProjectTotRegisterFactsBuilder` resolves, with today in IST:

| Fact | Rule |
| --- | --- |
| `Status`, `StatusLabel` | The ToT status; a project with no ToT record is Not started |
| `ProjectCompletedYear` | The project's completion date's year, else its recorded completion year |
| `YearsSinceProjectCompleted` | Today's year minus that year, in calendar years |
| `IsLongWaiting` | Not started and the project completed more than five calendar years ago (`LongWaitingYears`) |
| `Started`, `Completed` | ToT dates at their precision ("2004", "Nov 2025", "15 Oct 2026") |
| `YearsFromProjectToTot` | For Completed: ToT completion year minus the project's completion year |
| `Pending` | A request waiting for the HoD: proposed status, submitter, time, and only the fields it changes (status, started, completed, MET details and date, first production model and date), now against proposed |
| `LastDecision` | The latest approved or rejected request: who and when. Its reason is in the ToT history (`ProjectTotTrackerReadService.GetHistoryAsync`) |
| `Position` | Need ToT (all but Not required), Completed with its percentage, In progress, Not started, Not required, Waiting for decision, Long waiting |

### Deployment and rollback

Presentation and read-model changes on existing data; no migration, configuration or new
dependency. Each PR rolls back by reverting it, with one exception: once PR 4 is live and a
project holds Not known, reverting PR 3 would leave code that cannot read that row. Before
reverting PR 3, set those projects back to Not started (and note why in a ToT remark). Reverting
PR 4 alone is always safe: PR 3 still reads the value.
