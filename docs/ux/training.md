# Training tracker

The Training tracker records simulator and drone training sessions: when they ran, which projects
they relate to, and how many Officers, JCOs and ORs attended (from a roster, or as legacy totals).
Everyone with the View policy can read it; managers record and edit trainings; approvers decide
delete requests.

## Where it lives

| Concern | Code |
| --- | --- |
| Register (the module's main page) | `Areas/ProjectOfficeReports/Pages/Training/Index.cshtml(.cs)`; `/Training/Records` redirects here with its filters and page |
| Order and effective date | `TrainingTrackerReadService.OrderForRegister`, `TrainingListItem.EffectiveDate` |
| Record page, editor | `Pages/Training/View`, `Manage` |
| Officers / JCOs / ORs split | `Pages/Training/_StrengthSplit.cshtml` (model `IndexModel.StrengthBreakdown`) |
| Type colour key | `Pages/Training/TrainingTypeAppearance.cs` |
| Styles | `wwwroot/css/project-office-reports/training/register.css` (register), `manage.css` (shared tokens, tags, split, editor); site tokens only |
| Script | `wwwroot/js/project-office-reports/training/index.js` (trend chart, row clicks, sort/rows, export form) |

## The register (`/ProjectOfficeReports/Training`)

The most used parts come first and the chrome is kept small, so on a desktop screen the figures,
the technical categories, the trend and the first rows of the register are all in view.

1. **Header**, one row: the title, the scope ("All 341 trainings on record" or "10 trainings match
   your filters") and small action buttons: **Approvals** (approvers, with the pending count),
   **Export** (the workbook dialog, defaulting to the current filters), **New training** (managers).
2. **Headline figures** follow the filters: trainings (Simulator and Drone counts), trainees,
   Simulator trainees and Drone trainees, each trainee figure with its Officers / JCOs / ORs split.
   The split stays on phones.
3. **By technical category**, beside the yearly trend on wide screens: a card per category (all of
   them, most trainees first) with trainees, trainings and the rank split. The cards follow every
   filter except the category and project themselves, so they stay a stable way to move between
   categories. Choosing a card filters the register and shows that category's projects underneath,
   each with its session count; choosing a project filters by it, and its icon opens the project.
   Choosing the selected card or project again clears it.
4. **Register**, with a one-line filter bar: search (notes or project name), training type (applies
   at once), **More filters** (technical category, attended by, from, to; folded unless attended by
   or a date is set, with a count of the folded filters in use), sort (newest, oldest, most trainees,
   training type) and rows per page (25, 50, 100). Each active filter is a chip that removes just
   that filter; **Clear all** removes all. In date order the rows sit under month headings whose
   totals cover every matching training, not only this page.
   - A training is filed under its **effective date**: its start, else its end, else the first of its
     month for month-only entries. Month-only entries sort with the rest; undated ones come last.
   - A row shows the date block (day, month and year, then "to 16 Oct · 5 days", "1 day" or "Month
     only"), the first linked project (the link to the record) with "+N more", its units, the type tag
     (with a Legacy tag for totals-only entries), and the trainees: the total, then Officers, JCOs and
     ORs, each labelled and in its rank shade.
   - A click anywhere on the row opens the record (Ctrl/Cmd-click opens a new tab). Managers get
     **Manage**.
   - On phones each training is a card: date block and project, then type and trainees, then Manage.
     Nothing scrolls sideways.

## Colours

- **Training types.** Each type has one colour, used by its tag dot, its headline figure (a strip
  along the top of the tile) and its bar in the trend chart: Simulator `--training-type-simulator`
  (chart accent 1, blue), Drone `--training-type-drone` (chart accent 6, teal). Any other type
  uses `--training-type-other`. `TrainingTypeAppearance.Modifier` maps a type name to the
  `training-type--simulator|drone|other` class.
- **Officers / JCOs / ORs.** Three shades of the ink colour, darkest for Officers
  (`--training-rank-o|j|r`). They are neutral on purpose, so rank never competes with the type
  colours. Wherever a split is shown it is labelled: a bar, then "36 Officers · 120 JCOs · 509 ORs";
  in register rows, "8 O · 12 JCO · 40 OR" with the same shades.
- **Source.** Roster is the normal case and is not marked in the register; Legacy (historical
  totals, no roster) gets a tag in the soft warning colours.
- The training tokens derive from site tokens (`--pm-primary`, `--pm-text-strong`, `--pm-card`,
  `--pm-chart-*`), so Light and Dark need no separate values apart from the shadow.

## Trend chart

Trainees per training year (April–March), Simulator and Drone stacked. Colours, axis, grid and
legend come from the tokens above and `--pm-chart-*`, and the chart redraws when the theme changes.
The tooltip gives each type and the year's total. **Download** saves a PNG on the card colour.

## Record page

Details (period, total trainees, strength split, linked projects, notes with line breaks kept) and
the roster sit in separate cards with a gap between them; metadata sits beside them on wide screens.
The roster counters count roster rows; the strength split shows the training's stored totals.
