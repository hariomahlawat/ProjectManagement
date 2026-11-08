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
   with a **now strip**: days at the current stage and since when, SO value, started, elapsed, last update. Then **Journey**: the
   stepper with the days between stages above the line, plus a **where the time went** bar.
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
| Days between stages | Completion date minus the previous dated completion; blank when either date is missing ("date not recorded"). |
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

## Owner decisions needed

1. **Default sort** of the register: keep stage order (approved 8 Nov) with Longest wait as an
   option, or make Longest wait the default? The phone mockup uses Longest wait.
2. **Stage completions in the remarks feed**: show them in line (mockup) or keep remarks alone?
3. **Wording**: "Delivered" instead of "Completed", "Update stages" instead of "Stages".

## Plan (small PRs, each merged when Codex and CI are clean)

| PR | Content | Tests |
| --- | --- | --- |
| 1 | `StageMovedAtUtc` column and its setting in the stage save; `ItProjectTiming` in the read service: time figure with its fallbacks, gaps, elapsed, waiting longest, dates missing, on order, ageing band | rule tests for each fallback (previous stage dated, undated with and without a recorded move, earlier date not borrowed), skipped and dropped; migration manifest and tail test |
| 2 | Board header with FY segmented control and the four figure tiles | figures from the read model, FY filter links |
| 3 | "Where the cases are now" pipeline card | counts and ordering per stage, empty stages, overflow ("+3 more") |
| 4 | Register: track rows, Now column, age badges, legend, narrow optional stages, one filter bar, sort, "Dates missing" link for Manage | sort orders, band order, search still works |
| 5 | Project page hero and now strip; Record card takes Mark dropped and Delete; "At a glance" removed | page renders for Manage and read-only users |
| 6 | Journey: gaps above the line and the where-the-time-went bar | gap text with missing dates |
| 7 | Remarks and history feed with stage completions in line; Documents card polish | feed ordering, folded earlier stages |
| 8 | Stage editor as a vertical stepper with live consequence summary (reuses the Preview handler) | summary text, no change to the rules |
| 9 | Phones and dark theme pass for all three screens | layout at 375 px, theme coverage |
