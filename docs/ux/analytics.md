# Projects → Analytics

The Analytics page (`Pages/Analytics/Index.cshtml`) shows the project portfolio in four modes:
Ongoing, Completed, CoE and Insights. Redesigned on 2026-10-25 to the same ink-and-white rules as
the [Dashboard](app-shell-and-dashboard.md): numbers first, colour only where it means something,
one chart style defined once.

## Where it lives

| Concern | Code |
| --- | --- |
| Page, header, mode switcher, headline figures | `Pages/Analytics/Index.cshtml`, `IndexModel.Headlines` |
| Mode partials | `Pages/Analytics/Partials/_OngoingAnalytics.cshtml`, `_CompletedAnalytics.cshtml`, `_CoeAnalytics.cshtml`, `_ProjectManagementInsights.cshtml` |
| Styles | `wwwroot/css/analytics.css` (tokens only) |
| Chart style, legend chips, value labels, click-through | `wwwroot/js/charts/pm-chart-defaults.js` |
| Chart data wiring and PNG export | `wwwroot/js/analytics-projects.js` |
| Chart colour tokens | `wwwroot/css/site.css` (`--pm-chart-*`, Light and Dark blocks) |

## Page structure

- **Header**: the title "Analytics", one line saying what the active mode covers, the "As of" date
  (IST) on the right, and the 3px spectrum hairline under the title. That hairline is the only place
  the whole rainbow appears on the page.
- **Mode switcher**: one segmented control with four items and their counts (Ongoing, Completed,
  CoE, Insights). The content takes the full width; there is no side rail.
- **Headline figures**: three or four large figures open every mode. They are computed once in
  `IndexModel.BuildHeadlines()` from data the page already loads and link to the filtered projects
  list where a list exists.
  - Ongoing: projects, busiest stage, median days in the current stage, projects past 180 days.
  - Completed: total, this year, best year, median time from first stage start to completion.
  - CoE: total, ongoing, completed (with this year), sub-categories.
  - Insights: slowest stage, fastest stage, portfolio median (median of the stage medians), stages
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
- **Insights** has one stage cycle-time chart with two toggles (Median / Mean, All projects / By
  cost band) and the category filter, followed by "Where time goes": the stages ranked slowest
  first with plain-language numbers ("196 days median · 16× portfolio median · 8 projects"). A stage
  at two times the portfolio median or more is marked in the status red. The old Ongoing
  "Average time spent in each stage" chart is folded into this view.

## Chart rules (every Chart.js canvas in PRISM)

`applyPmChartDefaults(Chart)` in `pm-chart-defaults.js` sets the look once, from tokens:

- Mulish 12px axis text in `--pm-chart-text`; horizontal grid only (`--pm-chart-grid-soft`), no
  vertical grid, no chart border, no built-in legend or title.
- Bars: radius 6, `maxBarThickness` 44, values printed on the bars by the `pmValueLabels` plugin
  (stack total above, segment value inside when it fits, doughnut total in the hole). Every count on
  these pages is under 30, so labels replace a dense grid.
- Tooltips styled like PRISM cards (`--pm-chart-tooltip-*`), 300ms eased animation (0 under
  `prefers-reduced-motion`).
- Legends are HTML chips above the chart (`renderLegend`), with the series total; clicking a chip
  toggles the series.
- Click-through: `enableClickThrough(chart, urlFor)` opens the filtered list (Ongoing by present
  stage and parent category; completed by year, category or technical category) using the URLs on
  `#analytics-root`. Do not call `chart.update()` right after creating a chart; it freezes the first
  animation mid-frame.
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
