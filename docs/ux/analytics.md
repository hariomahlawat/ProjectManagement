# Projects → Analytics

The Analytics page (`Pages/Analytics/Index.cshtml`) shows the project portfolio in five modes:
Ongoing, Completed, CoE, Insights and ARPP. Redesigned on 2026-10-25 to the same ink-and-white rules as
the [Dashboard](app-shell-and-dashboard.md): numbers first, colour only where it means something,
one chart style defined once.

## Where it lives

| Concern | Code |
| --- | --- |
| Page, header, mode switcher, headline figures | `Pages/Analytics/Index.cshtml`, `IndexModel.Headlines` |
| Mode partials | `Pages/Analytics/Partials/_OngoingAnalytics.cshtml`, `_CompletedAnalytics.cshtml`, `_CoeAnalytics.cshtml`, `_ProjectManagementInsights.cshtml`, `_ArppAnalytics.cshtml` (with `_ArppGroupRow`, `_ArppYearFilter`); `_BoardRows.cshtml` is the shared drill-down list |
| Ongoing, Completed and CoE aggregation | `Services/Analytics/PortfolioAnalyticsService.cs` (`ProjectManagement.Tests/CoeAnalyticsTests.cs`); the page passes the IST "as of" day in |
| Insights aggregation | `Services/Analytics/ProjectAnalyticsService.cs` (`ProjectManagement.Tests/ProjectAnalyticsServiceStageTimeTests.cs`) |
| ARPP rules | `Services/Analytics/ArppAnalyticsService.cs` (`ProjectManagement.Tests/ArppAnalyticsServiceTests.cs`) |
| AR/VR proliferation and training (CoE mode) | `Services/Analytics/ArVrImpactAnalyticsService.cs` (`ProjectManagement.Tests/ArVrImpactAnalyticsServiceTests.cs`), over the proliferation tracker's approved aggregates and the training tracker's reach and training-year rules |
| Median and stage order shared by the three services | `Services/Analytics/AnalyticsAggregation.cs` |
| Styles | `wwwroot/css/analytics.css` (tokens only) |
| Chart style, legend chips, value labels, click-through | `wwwroot/js/charts/pm-chart-defaults.js` |
| Chart data wiring and PNG export | `wwwroot/js/analytics-projects.js` |
| Chart colour tokens | `wwwroot/css/site.css` (`--pm-chart-*`, Light and Dark blocks) |

## Page structure

- **Header**: the title "Analytics", one line saying what the active mode covers, the "As of" date
  (IST) on the right, and the 3px spectrum hairline under the title. That hairline is the only place
  the whole rainbow appears on the page.
- **Mode switcher**: one segmented control with five items and their counts (Ongoing, Completed,
  CoE, Insights, ARPP; ARPP only for users with the ViewArpp policy). The active pill is ink on the
  page colour, and its text takes the page colour too, so it reads in both themes. The content takes
  the full width; there is no side rail.
- **Headline figures**: three or four large figures open every mode. They are computed once in
  `IndexModel.BuildHeadlines()` from data the page already loads and link to the filtered projects
  list where a list exists.
  - Ongoing: one card per workflow phase, Pre-AoN (FS to AoN), Procurement (Bid to SO),
    Development (Devp) and Post-development (ATP to ToT), each with its project count and how many
    of those have sat in one stage past 180 days. The phase follows the project's current stage
    (the stage after the last completed one); a project with no stage at all is Pre-AoN. The one
    mapping is `Models/Stages/StagePhases.cs`; the chart draws the same four phases as brackets
    under its stage axis, and a card opens that phase's rows in the stage board. The total is the
    switcher badge and the header line (which says "n of N" when a parent-category filter is on).
    All four follow the parent-category filter (selecting every category is no filter); only the
    switcher badge stays the portfolio total. Days in stage are measured against the IST "as of" day.
  - Completed: total, this year, average completions a year over the last five complete calendar
    years (the partial current year is left out; a year with none counts as zero; a shorter history
    averages over what it has) with the five years before it for comparison, and the median time
    from first stage start to completion.
  - CoE (AR/VR): total, ongoing, completed (with this year), sub-categories. The switcher item is
    "CoE (AR/VR)". Below the sub-category board, "AR/VR projects started before the CoE
    nomination" lists by name the AR/VR projects (technical category) that are not in a CoE
    category, in three always-present groups: those started before the formal CoE (AR/VR)
    nomination, and the AR/VR projects under DCD as fresh projects and as repeat builds (the
    project's repeat-build flag). Under that, two cards for every AR/VR project (in or out of a CoE
    category): **AR/VR proliferation** (units delivered per calendar year, stacked SDD / ABW 515,
    approved records only, counted by the tracker's year-preference rule; receiving units from the
    granular ledger) and **AR/VR training** (people trained per training year, April to March,
    stacked officers / JCOs / other ranks; each course counted once in the totals and listed under
    each project it is linked to). A bar opens that year's row of projects. Each card is built and
    drawn only for a user its tracker's own view policy admits (ViewProliferationTracker,
    ViewTrainingTracker), checked server-side on the page. Each group lists completed projects first, oldest completion first, then ongoing with
    their current stage, then cancelled. These are not part of the CoE counts.
  - ARPP: listed projects (IPA value, New / CL / CF split, delisted), completed (their supply-order
    value), supply orders placed (ongoing projects, their value) and "AoN to SO stage" (ongoing
    projects not yet at a supply order, noting how many are still before AoN). Cancelled projects
    are in no card; the standing board lists them.
  - Insights: the three slowest stages (median days, ratio to the portfolio median, project
    count) and the portfolio median (median of the stage medians) with the number of stages
    measured.
- **Cards**: white (graphite in Dark) sheets with a hairline edge and `--pm-shadow-card`. Every
  card has the same header (title, one-line meta, actions on the right) and the Download button in
  the same place. Filters are one chip style (`analytics-filter`), whether a popover (Ongoing parent
  categories) or a select (Insights category).
- **Drill-downs**: the year, stage and sub-category boards are expandable rows
  (`<details class="analytics-drill__row">`): name, a proportional bar, category counts, the total,
  then the project list with links to each project's overview. No horizontal strips of tall cards.
  A CoE project row shows its current stage only while it is ongoing; completed and cancelled
  projects are identified by their lifecycle alone.
- **Insights** has one stage cycle-time chart with two toggles (Median / Mean; All projects / By
  parent category / By cost band) and the category filter, followed by "Where time goes": the
  stages ranked slowest first with plain-language numbers ("196 days median · 16× portfolio
  median · 8 projects"). A stage at two times the portfolio median or more is marked in the status
  red. The old Ongoing "Average time spent in each stage" chart is folded into this view. By parent
  category draws one series per root category (DCD, CoE, Other R&D, in the chart's category
  colours; "Uncategorized" in grey when it exists). The cost bands split at ₹2 crore, with a project
  costing exactly ₹2 crore in the lower band. Every finished stage counts in the All projects and
  By parent category views and in the ranked list; a project with no cost figure yet falls in
  neither cost band. The category filter means the category and everything under it.
- **Stage start dates** (every mode): stages run back to back, so a stage whose start was never
  recorded is measured from the day after its required predecessors finished, and any wait between
  stages counts as that stage's delay. The rule is `StageStartInference` in
  `Services/Stages/StageStartInference.cs` (`ProjectManagement.Tests/Stages/StageStartInferenceTests.cs`),
  the same one the project timeline uses for its "Inferred from preceding completion" start; it
  reads the project's own workflow graph, so parallel branches (TEC and BM before COB) take the
  later completion and a skipped stage is looked past. A completed stage never starts after its
  own completion (a same-day handoff is one day), a stage whose predecessor has no date is left
  out, and an ARPP-managed IPA keeps its start as recorded. It feeds the Insights cycle times and
  ranking, the Completed median time to complete (from the earliest recorded or inferred start)
  and the Ongoing days in stage. The inferred start is never stored. The Insights footnote says
  how many of the measured stages used one.

- **ARPP** (fifth mode, added 2026-10-25): the projects listed in one financial year's published
  ARPP (the latest published row per project, as the ARPP library's current position) joined to
  where each project stands today. A Financial year chip picks the year; the page opens on the
  current financial year when it has a published ARPP, else on the latest published year with a
  note. Cards: where the projects stand (ongoing by stage, with Completed / stage / Cancelled rows),
  by sanctioning authority (CFA), by category and IPA cost band, milestones (past AoN, supply
  orders and their value against listed IPA, development PDC due and overdue), and fund, DFPDS
  schedule and parent category rows. The rules (year choice, cost bands, standing by
  lifecycle, milestone eligibility: a supply order counts as placed for a project still in work, on a
  supply-order date or a stage beyond Supply Order, not on a resolved amount alone; completed
  projects are not in that count and carry their own supply-order value, shown on the Completed card
  and in the meter beside the ongoing value; PDC due and overdue count only projects still in work,
  and a closed year says so rather than showing an empty due window) live in
  `Services/Analytics/ArppAnalyticsService.cs` over `IArppLibraryService.GetCurrentPositionAsync` and
  `IArppFyProjectUpdateService.BuildAsync`; the page only binds the year.

## Staying on the page

Nobody has to leave Analytics to see which projects a figure is made of. Every chart has
expandable rows beneath it (`details[data-drill-key]`): a bar click opens and scrolls to its row
(`drillAction` in `analytics-projects.js`), and the rows list the project names with links to
each project's overview. Ongoing stages, completed years, completed categories and technical
categories, CoE stages (an "Unassigned" bar included) and sub-categories, and every ARPP grouping
all have rows.

## Chart rules (every Chart.js canvas in PRISM)

`applyPmChartDefaults(Chart)` in `pm-chart-defaults.js` sets the look once, from tokens. Analytics
(`analytics-projects.js`) and the Proliferation Summary charts
(`pages/project-office-reports/proliferation-summary-charts.js`, loaded as an ES module) both use it;
a new chart page should import it the same way rather than setting `Chart.defaults` or colour literals.

- Mulish 12px axis text in `--pm-chart-text`; horizontal grid only (`--pm-chart-grid-soft`), no
  vertical grid, no chart border, no built-in legend or title.
- Bars: radius 6, `maxBarThickness` 44, values printed on the bars by the `pmValueLabels` plugin
  (stack total above, segment value inside when it fits, doughnut total in the hole). Every count on
  these pages is under 30, so labels replace a dense grid.
- Tooltips styled like PRISM cards (`--pm-chart-tooltip-*`), 300ms eased animation (0 under
  `prefers-reduced-motion`).
- Legends are HTML chips above the chart (`renderLegend`), with the series total; clicking a chip
  toggles the series.
- Bar clicks: `enableBarClick(chart, resolve)` runs an action (open the row on the page) or opens a
  URL. Do not call `chart.update()` right after creating a chart; it freezes the first animation
  mid-frame.
- A theme switch (user menu → Theme) re-reads the tokens and redraws every chart (`watchTheme`).
- Every stage chart draws the same axis: all workflow stages in `StageCodes.All` order (from
  `data-stages` on `#analytics-root`), with the same short labels, then any ad hoc stage found in
  the data. Zero stages are shown, not hidden.

## Colour vocabulary

All chart colours are tokens in `site.css` with Dark lifts; nothing in JS or CSS is a literal.

| Meaning | Token | Light |
| --- | --- | --- |
| DCD Projects | `--pm-chart-cat-dcd` | blue |
| CoE | `--pm-chart-cat-coe` | green |
| Other R&D Projects | `--pm-chart-cat-other` | amber |
| Unassigned / other | `--pm-chart-cat-neutral` | grey |
| Ongoing / Completed / Cancelled | `--pm-chart-ongoing` / `-completed` / `-cancelled` | blue / green / grey |
| Single series, drill-down bars | `--pm-chart-primary` | blue |
| Second tint of a comparison (cost bands, mean vs median) | `--pm-chart-primary-soft` | lighter blue |
| Hotspot mark (≥ 2× portfolio median) | `--pm-chart-hotspot` | red |

Categories are resolved by name (`categoryColor`), so the doughnut, the stacked charts and the
drill-down dots always agree. A comparison is one hue in two tints, never two unrelated hues.
Green, amber and red otherwise stay reserved for status.

## Checks

- `npm test` runs `tools/theme-coverage.js` (no new colour literals) and
  `wwwroot/js/projects/portfolio-stage-consistency.test.js` (token-based category colours, shared
  axis headroom).
- Checked live on 2026-10-25 at 1920, 1400 and 375px in Light and Dark as an HoD user: no
  horizontal page scroll on phones, headline figures two-up, charts reflow.
