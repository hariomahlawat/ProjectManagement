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
| Record page, editor | `Pages/Training/View`, `Manage` (roster grid `_RosterGrid`) |
| Roster text (ranks, names, army numbers) | `Application/TrainingRosterText.cs`, applied on save (`TrainingWriteService`) and on read |
| Officers / JCOs / ORs split | `Pages/Training/_StrengthSplit.cshtml` (model `IndexModel.StrengthBreakdown`) |
| Type colour key | `Pages/Training/TrainingTypeAppearance.cs` |
| Styles | `wwwroot/css/project-office-reports/training/register.css` (register), `record.css` (record page), `editor.css` (editor), `manage.css` (shared tokens, tags, split, project picker); site tokens only |
| Script | `wwwroot/js/project-office-reports/training/index.js` (register: trend chart, row clicks, sort/rows, export form); `manage.js` (editor: schedule and legacy switches, roster grid) |

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
   categories. Choosing a card filters the register; choosing the selected card again clears it.
4. **Projects in the chosen category**, full width below the cards and the trend: full project names
   (they wrap, never truncate, because many share a long common start) in two columns (one on
   phones), most sessions first, each with its session count. The first ten show; **Show all N
   projects** opens the rest (it is already open when the selected project is among them).
   Choosing a project filters the register by it, again clears it, and its icon opens the project.
5. **Register**, with a one-line filter bar: search, training type (applies at once), **More
   filters** (technical category, attended by, from, to; folded unless attended by or a date is
   set, with a count of the folded filters in use), sort (newest, oldest, most trainees, training
   type) and rows per page (25, 50, 100). Each active filter is a chip that removes just that
   filter; **Clear all** removes all. In date order the rows sit under month headings whose totals
   cover every matching training, not only this page.
   - **Search** matches, case-insensitively and anywhere in the text: linked project names, units
     and trainees on the roster (name or army number), notes and the training type. Legacy
     trainings have no roster, so only their projects, notes and type are searchable. The export
     dialog's search uses the same rule.
   - The address keeps only the filters in use: blank filters and the default sort and rows are
     left out, so a filtered view can be bookmarked or shared as a short link.
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

## Record page (`/Training/View?id=…`)

Same shape as the Visits record page.

- Breadcrumb back to the tracker, then a plain header: the type tag (and **Legacy totals** for
  totals-only records), the title, and a line with the dates and length, the number of trainees and
  the units. The title is what the training served: its project, "Project and N more" when it served
  several, or "Simulator training" when no project is linked. **Edit training** for managers.
- **Who attended**: the total, the Officers / JCOs / ORs split (the training's stored totals), then
  the roster, rank and name first, with army number, unit and category. A totals-only record says
  there is no roster and why. On phones each person reads as two lines.
- **Projects**: each linked project, linking to its project page. **Notes** as typed, line breaks kept.
- Beside them (below on narrow screens): **The training** (type, dates, length, trainees, recorded as
  a roster or as totals) and **Record**: who recorded it and when, and who last edited it and when,
  in IST. An edit within two minutes of recording does not count as an edit.

## Recording and editing a training (`/Training/Manage`)

- Same width and breadcrumb as the other pages; **Record a training** or **Edit training**.
- Three cards: **What and when** (type, then "Dates known": exact dates or month and year only, as
  two side-by-side choices, then the matching fields), **Projects and notes**, and **Who attended**.
- **Who attended** holds either the roster (counts by category update as you type; **Add person**
  puts the cursor in the new row) or, with **Totals only (legacy record)** switched on, three totals
  for registers that have no names.
- Roster text is tidied when it is saved and when it is shown (`TrainingRosterText`): ranks and names
  are capitalised word by word unless already in ordinary mixed case ("LT COL" → "Lt Col", "sURYAMAN
  sHUKLA" → "Suryaman Shukla", "McGregor" kept), army numbers lose inner spaces and are upper case,
  units keep their case. Existing rows are shown tidied without a data migration and are stored
  tidied on their next save.
- The roster travels to the server as base64 of UTF-8 JSON, so names in any script (and curly
  apostrophes) are kept.
- **Save** sits in a bar that stays in view at the bottom of the screen on desktop; on phones it is
  at the end of the form. **Cancel** returns to the record (or the tracker for a new one).
- **Request deletion** is set apart below the form, folded until opened; it sends a request that an
  approver decides, and a pending request is shown at the top of the editor with its reason and time
  (IST).
- Switches, radios and checkboxes in the editor have a 3:1 outline in both themes; each card has a
  visible edge (cards are not the page colour).
