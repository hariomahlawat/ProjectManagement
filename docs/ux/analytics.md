# Projects → Analytics

The Analytics page (`Pages/Analytics/Index.cshtml`) shows the project portfolio in five modes:
Ongoing, Completed, CoE, Insights and ARPP. Redesigned on 2026-10-25 to the same ink-and-white rules as
the [Dashboard](app-shell-and-dashboard.md): numbers first, colour only where it means something,
one chart style defined once.

The HoD and the Comdt reach it from **Project analytics** under Command oversight in My Workspace
(it replaced the workspace's own Project portfolio chart on 27 Oct 2026; see
[App shell](app-shell-and-dashboard.md#my-workspace-page-headers)). Every signed-in user can open
the page; only the ARPP mode checks a further policy.

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
| CoE slides download | `Services/Analytics/Presentation/CoeSlides.cs` (data, from the page's view models) and `CoeSlideComposer.cs` (drawing), on the shared canvas `Services/Presentation/PptxSlideCanvas.cs` that the FFC deck also uses (`ProjectManagement.Tests/Analytics/CoeSlidesTests.cs`) |

## Page structure

- **Header**: the title "Analytics", one line saying what the active mode covers, the "As of" date
  (IST) on the right, and the 3px spectrum hairline under the title. That hairline is the only place
  the whole rainbow appears on the page. On the CoE mode (when it has projects) a **Download slides**
  button sits beside the date: see "CoE slides" below.
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
    A project filed straight under a CoE parent category (a CoE category whose own parent is not
    one) reads "<parent> (no sub-category)" in the chart and the board, and is not counted as a
    sub-category; the headline hint says how many projects are in none.
  - ARPP (five cards): listed projects (IPA value, New / CL / CF split, delisted), completed (their
    supply-order value), supply orders placed (ongoing projects, their value), "AoN to SO stage"
    (ongoing, AoN completed, no supply order yet) and "Pre-AoN stage" (ongoing, AoN not yet
    completed). Cancelled projects are in no card; the standing board lists them. On two columns
    the Listed card takes the full row.
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
- **Downloaded charts** (the Download button on every chart card): a 1920 × 1080 PNG drawn by
  `exportChartAsPngFullHd` in `wwwroot/js/analytics-projects.js`. It uses the chart's own value
  labels (`pmValueLabels`, so the same number format and fit rules as the page) at twice their
  screen size, with bars 1.6 times as thick and phase names under the Ongoing stage axis grown
  to match. A label that does not fit is left out rather than spilling: a segment value too wide
  for its bar, the inside label of a one-segment stack (the total already says it), and the
  smaller of two side-by-side values that would collide. A doughnut adds each slice's value on
  the ring where it fits and in the legend. The caption under the chart is the card's title and
  meta without its "click a bar…" hint, then the mode, the card's toggles and its filters (or the
  mode's, for ARPP's financial year) and the time. Series switched off with a legend chip stay
  off. The export takes the current theme's colours.

- **ARPP** (fifth mode, added 2026-10-25): the projects listed in one financial year's published
  ARPP (the latest published row per project, as the ARPP library's current position) joined to
  where each project stands today (for a past year: where it stood on 31 March, the last day of that
  year, worked out by `ProjectPositionAsOfService`, with a Today switch beside the year). A Financial year chip picks the year; the page opens on the
  current financial year when it has a published ARPP, else on the latest published year with a
  note. Cards: where the projects stand (ongoing by stage, stacked by parent category like the Ongoing
  chart, with a legend that names only the categories present in that year's ARPP, in their own sort
  order; the Completed / stage / Not known / Cancelled rows carry the same category chips and list their
  projects under each),
  by sanctioning authority (CFA), by category and IPA cost band, milestones (past AoN, supply
  orders placed, and the supply-order amounts beside the listed IPA, no percentage), and fund,
  DFPDS schedule and parent category rows. No PDC figures: they are development tracking (Command
  desk, officer conference). The rules (year choice, cost bands, standing by lifecycle, milestone
  eligibility: past AoN is the AoN stage marked Completed, with or without its date ("date not
  recorded" in the list), or Skipped ("AoN skipped"), and a workflow without an AoN stage is past
  AoN once its stage is beyond Pre-AoN; a supply order counts as placed for a project still in
  work, on a supply-order date or a stage beyond Supply Order, not on a resolved amount alone;
  completed projects are not in that count and carry their own supply-order value, shown on the
  Completed card and beside the ongoing value) live in
  `Services/Analytics/ArppAnalyticsService.cs` over `IArppLibraryService.GetCurrentPositionAsync` and
  `IArppFyProjectUpdateService.BuildAsync`; the page only binds the year.

### ARPP review (6 Nov 2026): plan

A review of the ARPP mode with dummy data covering every case (dev database: FY 2026-27 published,
39 listed projects; scripts outside the repo). The money adds up: every grouping (CFA, category,
cost band, fund, DFPDS schedule, parent category) sums to the listed IPA, an addendum's revised IPA
replaces the original and a delisted project drops out. The codes (CL, CF, CFA, DFPDS schedules,
stage codes) are the organisation's own and stay as they are. The owner's decisions:

- **Past AoN is the AoN stage marked Completed**, with or without a date. Today the milestone needs
  the completion date, so a project whose AoN is Completed without one (three in the dev data) is
  missing from "Past AoN" while the cards count it as past AoN by stage. One rule for both; the
  drill-down says "date not recorded".
- **Cards**: Listed · Completed · Supply order placed · AoN to SO stage · **Pre-AoN stage**. "AoN to
  SO stage" counts only projects with AoN completed and no supply order; Pre-AoN is FS, IPA, SoW and
  the AoN stage itself (AoN not yet granted). Cancelled projects stay out of the cards (the standing
  list shows them). A listed project cannot be archived while active, so archived projects need no
  card.
- **Supply-order value**: PNC/L1 is the order value once the supply order is placed (no change).
  The amounts stay ("₹2.66 Cr ongoing · ₹1.42 Cr completed, of IPA ₹203.85 Cr listed"); the
  percentage bar goes.
- **No PDC on this page.** "PDC due" and "PDC overdue" are development tracking, already on the
  Command desk and the officer conference view; the ARPP page is listing → AoN → supply order →
  completed.
- **A past year shows the position on 31 March**, the last day of that financial year, with a
  **Today** switch to where those projects stand now. The current year is as of today (no switch).
  The position on 31 March is worked out, never stored:
  - a stage is finished by 31 March when its completion date is on or before it;
  - a Completed stage with no date ended the day before the next stage started (stages run back to
    back, the rule `StageStartInference` already applies forwards), and is finished by 31 March when
    a later stage had started or finished by then;
  - the stage change log (`StageChangeLogs`, kept since 27 Nov 2025) settles the rest: a stage
    recorded In progress on a date means the stages after it were not finished on that date, and a
    change recorded on or before 31 March happened by then;
  - the project's own workflow graph decides "next" and "later" (parallel TEC and BM, skipped stages
    looked past), as `StageStartInference` does;
  - when the exact stage is still open but every candidate sits in one card ("SoW or AoN"), the
    project counts in that card and the list says "SoW or AoN (dates not recorded)";
  - what cannot be placed, and a project completed or cancelled with only a year (legacy data),
    goes in a "Not known on 31 Mar" group, never guessed into a card.

PRs:

1. **Cards and rules.** Done (this change). Past AoN by stage status; the Pre-AoN card; "AoN to SO" narrowed to AoN
   completed without a supply order; PDC rows and the percentage bar removed; "1 published row"
   singular. Tests in `ArppAnalyticsServiceTests`.
2. **Position on a date.** Done (this change): `StageStateAsOfResolver` (pure rule) and
   `ProjectPositionAsOfService` in `Services/Stages`, tests in `StageStateAsOfResolverTests`. A past
   year's cards, standing list, chart and milestones count the position on 31 March; a project the
   records cannot place in one stage is listed under "Not known on 31 Mar" (with its range, such as
   "SOW Vetting or Acceptance of Necessity (dates not recorded)") and still counts in its card when
   every candidate stage sits in one; one whose card cannot be told is counted on the Listed card
   ("N not known on 31 Mar"). Supply-order values are today's (a placed order's PNC/L1 value does
   not change). One resolver for "where was this project on date D"
   (`Services/Stages`, with its own tests: dated stages, the next-stage start, the change log,
   parallel branches, skipped stages, the one-card range and Not known). The ARPP mode uses it for
   a past year's 31 March; nothing else changes.
3. **Today switch** for a past year. Done (this change): beside the Financial year chip, "On 31 Mar 2026 |
   Today" (links, `?view=today`, so a link opens the same view; the year chip keeps the Today view). The
   current year has no switch.

### ARPP progress slides (6 Nov 2026)

The **Slides** button beside the Financial year chip downloads the ARPP as two slides, or three when a
column runs long (`OnGetArppSlidesAsync`, the same ViewArpp check as the mode; the year and the 31 March
/ Today view of the page). `ArppProgressSlideComposer` (`Services/Analytics/Presentation`, tests in
`ArppProgressSlideComposerTests`) draws them as PowerPoint shapes on the Briefing Studio template, with
the ARTRAC insignia left and the SDD insignia right, no handling marking, and "Generated by PRISM ERP on
<date>" in every footer:

- **Slide 1:** the five headline cards (`ArppHeadlines`, the same builder as the page; the supply-order
  card reads "Under development") and the stage chart stacked by parent category, its legend naming only
  the categories present.
- **Slide 2:** every listed project in four columns (Pre-AoN, AoN to SO, Under development, Completed),
  full names wrapped, a category dot (legend in the header), the stage tag before the supply order, the
  PDC at Development (red when overdue; no stage tag in that column), and the completion month and year.
  Furthest along first. Cancelled and not-known projects are named in the footer. A column that does not
  fit continues on a further slide.

The slides count nothing themselves: the project lines (`ArppAnalyticsVm.ProjectLines`) come from the
same standings as the cards.

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

## CoE slides

`?handler=CoeSlides` on the CoE mode downloads a two-slide widescreen PowerPoint (more, when a list needs continuation slides), built on the server
from a bundled template (no network access). Both slides are drawn from the same view models as the
page (`CoeAnalyticsVm`, `ArVrImpactVm`); nothing is recomputed for the slides. The look follows the
ARPP slide style: a white page, the formation crest (left) and SDD insignia (right) either side of the
title over a navy rule, tinted column headings, and a footer note with the page number.

1. **CoE (AR/VR) roadmap**: one column per CoE category (real sub-categories by name,
   then "(no sub-category)"), each listing its projects: ongoing first, furthest workflow stage
   first, then completed. Every name is shown in full, wrapping where needed; the stage's short label
   (the stage chart's axis label, `StageCodes.ShortLabelOf`) or "Completed" sits on the right of the
   first line. A project in Development shows its PDC (the current stage's planned due date, as on
   the project header) on the line under its name, in red with "overdue" once it has passed. One type
   size serves the whole slide: the largest at which the longest column fits. Cancelled projects are
   left out of the columns and named in the footer.
2. **AR/VR completed projects, proliferation and user training**: three stat cards (AR/VR projects completed, in the CoE and outside it;
   people trained and courses; units proliferated and receiving units), then every completed AR/VR
   project by name with its completion year, newest first, in balanced columns
   (`CoeAnalyticsVm.ArVrCompleted`: the CoE, before the CoE nomination, DCD fresh, DCD repeat
   builds). The training and proliferation cards appear only for a user their tracker's view policy
   admits, the same check as the page cards.

Slide 1 holds at most four category columns (about 3 in each); more categories are spread evenly over
slides ("(continued)"; 5 make 3 + 2). When a list will not fit even at 7.5 pt (row heights count every wrapped line of the name and of the
PDC), it carries on to continuation slides at 10 pt, titled "(continued)": slide 1's columns keep
their headings and totals, and the completed list fills four columns a page with the cards on the
first page only. Nothing is drawn past the slide edge.
