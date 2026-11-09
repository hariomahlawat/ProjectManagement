# IT projects: UI and UX redesign (`/ItProjects`)

Review of the module as built in PRs #3020–#3034 ([it-projects.md](it-projects.md)), checked in the
browser on 8 Nov 2026 against the local demo set (12 cases, remarks, attachments) on desktop,
phone width and dark theme. Mockup: [mockups/it-projects-redesign.html](mockups/it-projects-redesign.html)
(four screens: board, project page, stage editor, phone; light and dark).

This changes presentation only. The owner decisions in `it-projects.md` (nine stages, rules,
roles, no plan, no approval) stay as they are; the derived figures below are computed once in the
read service, never in Razor or JavaScript.

## What is wrong today

The module works, but it reads as a default admin table. Every row looks the same, so the page
does not answer the one question the HoD and Comdt open it for: **which case is stuck, and for
how long?**

| # | Where | Finding |
| --- | --- | --- |
| 1 | Board | No sense of time. "Core switches" has sat at COB for 355 days and "Campus Wi-Fi" at Delivery for 331 days; both look exactly like a case that moved last week. |
| 2 | Board | The nine equal stage columns are a grid of identical circles. Benchmarking and PNC take full columns but are skipped on 9 of 12 cases, so a fifth of the width is dashes. |
| 3 | Board | Summary tiles are weak: "Most projects at: Delivery and ATP, 2 waiting there" is trivia, "Completed 3 / 0 this FY" and "Dropped 1 / kept for the record" repeat the status pills just below. Nothing shows money on order. |
| 4 | Board | The current stage label is "now" under a blue dot; you have to read the column header to know which stage it is. |
| 5 | Board | Filters are spread over four controls (pills, FY select, Group-by toggle, search) of four different styles. |
| 6 | Project page | The stepper shows dates but not the gaps between them, which is where the story is (Bid took 71 days, Delivery 331 and counting). |
| 7 | Project page | "At a glance" repeats the header (status, FY, SO cost) and adds only "Created". |
| 8 | Project page | Remarks and stage changes are separate worlds: the remark list has no context of when stages closed. |
| 9 | Project page | Destructive and rare actions (Mark dropped, Delete) sit at the same weight as Edit and Stages, and Delete floats alone above the footer. |
| 10 | Stage editor | A flat list of checkboxes and native date inputs. The consequence of a tick (earlier stages auto-completing) is only told in a confirm dialog after Save. |
| 11 | Phone | The board's nine-dot stepper has no labels, so a card says "now" without saying where. The project stepper wraps into a 3 × 3 grid and loses its order. |
| 12 | Wording | "Completed" for a case whose last stage is Delivery and ATP; "Stages" as a button label. |

## The concept: a pipeline that shows waiting time

1. **Figures that matter** (four tiles with a spectrum mark): In progress, **Waiting longest**
   (days and the case), **On order, not yet delivered** (sum of SO value on cases at Delivery),
   **Delivered** (count, this FY, last delivery date).
2. **Where the cases are now**: a chevron pipeline of the nine stages; under each, the live cases
   at that stage as small cards with their time figure, edged amber or red when stage dates show
   them ageing.
   Optional stages are narrower and hatched when unused. Each card opens the case.
3. **Register**: one row per case with a **track** (a line with the stage dots on it, filled to
   the current stage, which pulses) and a **Now** column ("At COB", the time figure from the
   rule below, SO value). A completed stage without a date shows a hollow tick. Dates appear on row hover. Delivered cases show "Delivered 30 Jun 25 ·
   356 days end to end". Status, sort (Stage order / Longest wait), FY and search sit in one bar;
   FY becomes a segmented control in the header.
4. **Project page**: a hero card (reference, FY and status chips, title, description, actions)
   with **one status line**: where the case is and for how long ("At Delivery and ATP for 332 days ·
   since SO closed on 12 Dec 2025"), plus a New / CL / CF chip for a carried-over case. Then
   **Journey**: the stepper with the days between stages above the line. (A five-card "now strip"
   and a "where the time went" bar were built and then removed on 9 Nov 2026: they repeated what the
   page already shows. Keep the page lean; no extra metrics.)
   Below, two columns: **Remarks and history** (composer, then remarks with stage completions in
   line, older stages folded) and **Documents** (file rows, drop zone) plus a small **Record**
   card that holds Mark dropped and Delete.
5. **Stage editor**: a vertical stepper; ticking a stage shows the stages it will complete
   (dashed) before Save, each row shows its gap from the previous date, and a live summary says
   what Save will do. Native date pickers stay (offline, accessible).
6. **Phone**: horizontally scrolling figure tiles, cards with a nine-segment bar plus "At COB ·
   stage 5 of 7" and the age badge, a vertical journey on the project page, and a sticky
   Remark / Update stages bar.

All colours are existing `--pm-*` tokens (theme coverage check), no new assets, nothing online.

## Derived figures (one place: the read service)

| Figure | Rule |
| --- | --- |
| Days at current stage | Uses the first of these that exists. (1) **Stage date**: today minus the completion date of the stage just before the current one (skipped stages ignored), on the PRISM stage start rule. This is the real figure and the only one that gets an ageing colour. (2) **Recorded move**: if that stage has no date, the day the system recorded the move to the current stage (`StageMovedAtUtc`, below), shown as "recorded 21 Aug" in light text, never coloured and never in "Waiting longest". (3) Otherwise "date not recorded". An earlier stage's date is never borrowed, because it would add the earlier stages' time to this one. A case with no stage done shows "added 5 d ago". |
| Days between stages | Completion date minus the completion date of the stage just before it (skipped stages ignored); blank when either date is missing, never measured from an earlier stage. |
| Elapsed | Today (or the Delivery date) minus the first dated completion. |
| Waiting longest | Maximum days-at-stage over active, non-dropped cases in the current filter, from stage dates only; the tile says how many cases were left out for missing dates. |
| Dates missing | Active cases with a completed stage that has no date. Shown to Manage roles only, as a link that filters the register to them. |
| On order | Sum of SO cost on active cases whose SO is complete and Delivery is not. |
| Ageing band | From the stage-date figure only, the same for every stage (owner decision 8 Nov 2026): up to 30 days normal, 31–60 amber, over 60 red. |

`StageMovedAtUtc` is one new nullable column on `ItProjects` (hand-written, additive migration;
existing rows stay null, which simply means fallback 3). The stage save sets it only when the
current stage changes, so editing the title or a date does not count as movement. Bulk entry of an
old case sets it to the entry day, which is why it is shown as "recorded", not as a duration.

These go on the existing board and details view models (one `ItProjectTiming` projection), with
unit tests for each rule including missing dates and skipped stages.

## Year type: New, CL and CF (owner decision 8 Nov 2026)

A case that is not finished in its own financial year carries over into the next one, and its
type for that year changes.

| Type | When |
| --- | --- |
| **New** | The year is the case's own IT ARPPP financial year |
| **CL** (committed liability) | A later year, and the SO was placed: the SO stage was complete before that year began (an SO marked complete without a date counts as placed) |
| **CF** (carried forward) | A later year, and the SO was not yet placed |

- The type is **derived per year**, never typed in on 1 April: the case keeps its own financial
  year, and each later year it is live in gives it a type. Looking back at 2025-26 shows what was
  New or carried in that year.
- A case is **live in a year** from its own financial year up to the current one, until it is
  delivered (Delivery and ATP date) or dropped (dropped date) before that year begins. A delivery
  without a date keeps the case in its own year only.
- **A new IT ARPPP number each year**: CL and CF cases are listed again in the next year's IT ARPPP
  under a new IT ARPPP reference number. The case keeps its first number on the project; each later
  year holds its own number, entered by the ITO, HoD or Admin when that year's IT ARPPP is made
  ("number not entered" until then). Search finds a case by any year's number.
- **Change by hand**: the same people can set the type of one case for one year (New, CL or CF),
  or go back to the automatic type. Both are stored in `ItProjectFinancialYear` (one row per case
  and later year: reference number, optional type), audited as `ItProjects.YearChanged`. The first
  year's number is changed with Edit.
- The board's **financial year filter** shows every case live in that year, each with its type
  chip, and the figure tiles split by type ("8 in progress · 3 New · 2 CL · 3 CF").

## Owner decisions (8 Nov 2026)

| Topic | Decision |
| --- | --- |
| Design | Mockup approved |
| Time figure | Stage dates first; recorded move as a light fallback; never borrow an earlier stage's date (rule above) |
| Ageing bands | Up to 30 days normal, 31–60 amber, over 60 red; one rule for every stage |
| Register order | Stage order by default, Longest wait as an option (the phone list uses Longest wait) |
| Remarks feed | Stage completions shown in line with the remarks |
| Wording | "Delivered" instead of "Completed"; "Update stages" instead of "Stages" |
| Simplicity (9 Nov) | No metrics that repeat the page: the project header has one status line, no card strip and no time bar |
| Year type | New / CL / CF per year as above, CL when the SO is placed, changeable by hand; a carried-over case gets a new IT ARPPP number in each later year |

## Plan (small PRs, each merged when Codex and CI are clean)

PR 1 (#3036) is merged. The year type was added on 8 Nov as PR 2, so the later PRs moved down one.

| PR | Content | Tests |
| --- | --- | --- |
| 1 | `StageMovedAtUtc` column and its setting in the stage save; `ItProjectTiming` in the read service: time figure with its fallbacks, gaps, elapsed, waiting longest, dates missing, on order, ageing band | rule tests for each fallback (previous stage dated, undated with and without a recorded move, earlier date not borrowed), skipped and dropped; migration manifest and tail test |
| 2 | Year type: `ItProjectYearTypes` rule, `ItProjectFinancialYear` table (later-year IT ARPPP number and type set by hand) and migration, save command with audit, read methods, search by any year's number | rule per year (own year, SO placed or not, undated SO, delivered or dropped before the year), override wins and resets, Manage only, migration manifest |
| 3 | Board header with FY segmented control (cases live in the year, type chips) and the four figure tiles split by type | figures from the read model, FY filter links |
| 4 | "Where the cases are now" pipeline card | counts and ordering per stage, empty stages, overflow ("+3 more") |
| 5 | Register: track rows, Now column, age badges, legend, narrow optional stages, one filter bar, sort, "Dates missing" link for Manage | sort orders, band order, search still works |
| 6 | Project page hero (#3041); Record card takes Mark dropped and Delete; "At a glance" removed | page renders for Manage and read-only users |
| 7 | Journey: days between stages above the line; the hero's card strip becomes one status line | gap text with missing dates, status line per kind of case |
| 8 | Remarks and history feed with stage completions in line; Documents card polish | feed ordering, folded earlier stages |
| 9 | Stage editor as a vertical stepper with live consequence summary (reuses the Preview handler) | summary text, no change to the rules |
| 10 | Phones and dark theme pass for all three screens | layout at 375 px, theme coverage |
