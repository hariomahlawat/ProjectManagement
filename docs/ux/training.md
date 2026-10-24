# Training tracker

The Training tracker records simulator and drone training sessions: when they ran, which projects
they relate to, and how many Officers, JCOs and ORs attended (from a roster, or as legacy totals).
This note covers the colours and figures shared by its pages. The register itself (tracker plus
the full records list) has not been redesigned yet.

## Where it lives

| Concern | Code |
| --- | --- |
| Tracker, records list, record page, editor | `Areas/ProjectOfficeReports/Pages/Training/Index`, `Records`, `View`, `Manage` |
| Officers / JCOs / ORs split | `Pages/Training/_StrengthSplit.cshtml` (model `IndexModel.StrengthBreakdown`) |
| Type colour key | `Pages/Training/TrainingTypeAppearance.cs` |
| Styles | `wwwroot/css/project-office-reports/training/manage.css`; site tokens only |
| Script | `wwwroot/js/project-office-reports/training/index.js` (trend chart, export form) |

## Colours

- **Training types.** Each type has one colour, used by its tag dot, its headline figure (a strip
  along the top of the tile) and its bar in the trend chart: Simulator `--training-type-simulator`
  (chart accent 1, blue), Drone `--training-type-drone` (chart accent 6, teal). Any other type
  uses `--training-type-other`. `TrainingTypeAppearance.Modifier` maps a type name to the
  `training-type--simulator|drone|other` class.
- **Officers / JCOs / ORs.** Three shades of the ink colour, darkest for Officers
  (`--training-rank-o|j|r`). They are neutral on purpose, so rank never competes with the type
  colours. Wherever a split is shown it is labelled: a bar, then "36 Officers · 120 JCOs · 509 ORs".
  In tables the column reads "Strength (O · JCO · OR)" with the numbers separated by middle dots.
- **Source.** Roster is the normal case and gets a quiet outlined tag; Legacy (historical totals,
  no roster) gets the soft warning colours.
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
