# Briefing decks (`/Workspace/BriefingDecks`)

Comdt and HoD users build shared PowerPoint briefing decks from live PRISM data. This document
records what the module does today, the problems found in the review of 1 Nov 2026, and the
agreed plan to turn the builder into a **Briefing Studio**.

## Where it lives

| Concern | Code |
| --- | --- |
| Page | `Pages/Workspace/BriefingDecks/Index.cshtml(.cs)`; library `_DeckLibrary`, `_DeletedDecks`; editors `_InstitutionalProfileEditor`, `_RoleCharterEditor`, `_FfcGlobalFootprintEditor` |
| Deck persistence | `Services/ProjectBriefings/ProjectBriefingDeckService.cs`; options in `SelectionRulesJson` via `ProjectBriefingDeckConfigurationCodec` |
| Page data, readiness, slide estimate | `Services/ProjectBriefings/ProjectBriefingDataService.cs` |
| Slide generation | `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer*.cs`, `ProjectBriefingPowerPointExportService.cs` (`ComposeAsync` builds the deck; `GenerateAsync` also records the generation), `ProjectBriefingPresentationIntegrityValidator.cs` |
| Studio | `ProjectBriefingSlidePreviewRenderer.cs` (composed PowerPoint → positioned HTML); slide roles from `ProjectBriefingSlideComposer.ComposeWithOutline`; content gaps from `ProjectBriefingContentGaps`; page handler `OnGetPreviewAsync`; `wwwroot/js/pages/project-briefing-studio.js` |
| Scripts and styles | `wwwroot/js/pages/project-briefing-decks.js`, `project-briefing-library.js`, `project-briefing-additional-slides.js`, `project-briefing-studio.js`; `wwwroot/css/pages/project-briefing-decks.css` |
| Access | `Policies.ProjectBriefingDecks.Manage` (Comdt, HoD); every deck is shared |
| Tests | `ProjectManagement.Tests/ProjectBriefings/*`, `wwwroot/js/projects/project-briefing-*.test.js` |

## Deck library (landing page)

`/Workspace/BriefingDecks` without a deck id is the library (5 Nov 2026); it no longer opens the
last-updated deck, so nobody edits a deck they did not pick.

- **New deck** asks for a name and a template (Portfolio or Fact Sheets), then opens the
  Studio on Add projects.
- **Continue**: the deck the person last edited (else the latest), with Open in Studio and
  Generate PowerPoint.
- **All decks** table: deck and description, template (Portfolio or Fact Sheets, from the
  deck's slide design),
  projects, slides in the last generated PowerPoint (`LastGeneratedSlideCount`, recorded on each
  generation; "—" until then), owner, last edited, last generated, and a ⋯ menu (Open, Generate,
  Duplicate, Delete through the Studio confirmation dialog). **Mine** keeps decks the person owns
  or last edited; search also matches the names of projects in a deck; sort by last edited, last
  generated or name. Filtering runs in the page (`project-briefing-library.js`).
- **Recently deleted** below the table.
- `/Workspace/BriefingDecks/{id}` is the Studio. Its bar has "← Briefing decks" and **Switch
  deck** (each deck's purpose and project count); the old saved-decks side panel is gone. An
  unknown or deleted id returns to the library with a one-request notice.
- **Data moved since last generated** (PR 2). Each generation keeps, per presented project, the
  facts its slides drew and which ones they drew (`LastGeneratedFactsJson`,
  `ProjectBriefingSlideFacts`): the stage, read as the slides read it (its name where a table row, detail slide or the stage summary shows it; its maturity where it orders two or more projects' slides; its lifecycle for a standard project slide's subtitle (capability slides and brief continuations), the portfolio summary's counts and the stage summary's status band; the update sheet's milestone row exactly as `ProjectBriefingUpdateSheetPlanner.DescribeMilestone` draws it: the PDC date in development, else the completion or cancellation), the external status remark (date and a digest of its text) and the costs
  drawn (the R&D cost, with its basis label where cost cards or the executive table draw it, the proliferation cost by cost mode, the IPA cost in
  an update sheet's portfolio totals). `ProjectBriefingSlideFacts.Shown` mirrors the composer:
  "Show present stage/status", the update sheet's selected rows, table-row-only projects, the
  summary slides and the cost mode. The library rebuilds today's facts with
  `GetCurrentSlideFactsAsync` (the same cost resolver, external status service and stage rule as
  generation) and compares only what each project's slides showed, so a hidden fact or a settings
  change since never counts (a project completing can also revise its cost, by the shared cost
  rule). Shown as an amber line on the Continue card, a Data since column ("N changes", "Up to
  date", "Not generated yet", or "—" for decks generated before facts were kept) and a Needs
  attention strip of up to three other moved decks with the projects that moved. Deleted or
  archived projects are left out, as generation leaves them out. Content checks are not counted
  on the library: they would mean composing every deck on each visit.

## Studio

The top of the builder is the Studio: the slide outline, the selected slide and an inspector.
The server composes the same PowerPoint that **Generate PowerPoint** downloads (without
recording a generation) and reads it back into positioned HTML: shapes, text, lines, pictures,
tables and groups, in the composer's own colours. So the slides and their count are exact, not
estimated. The composer also reports what each slide is (`ProjectBriefingSlideRole`, plus the
project for project slides), which the outline uses instead of reading slide text. Pictures are
shrunk to 960px and sent once each, however many slides use them. Every text value is encoded on
the server; the script inserts only slide HTML and writes all other text as text.

- **Outline** (left): Opening, Portfolio, one group per present stage (in the deck's maturity
  order), then Closing. A project's slides share one row; runs of status-table, category and FFC
  breakdown pages fold into one row. Project rows carry an amber count of missing content.
  Search filters by title, stage and category. Drag a project row by its grip, or press Alt+Up or
  Alt+Down on it, to move it within its stage; the generator groups projects by present stage, so
  order is only ever changed inside a stage (bug 4). Left-out (deleted or archived) projects keep
  their place. **Projects** opens Add projects; **Slide** opens the additional-slide library. A
  status line under the search reports membership and order saves.
- **Slide** (centre): the selected slide as large as the pane allows, or **All slides** as a grid
  that adds columns on wider screens. Arrow keys and the arrow buttons move between slides;
  **Present** opens the full-window viewer (strip beside the slide from 1400px, below it on
  narrower screens; Page Up/Down, Home, End, Escape) and returns to the slide it ended on. Under
  a project slide, each missing item has its fix: write the capability overview for this deck,
  or open the project, its Photos page or its brief.
- **Inspector** (right): **This slide** shows a project's costs, status remark, capability
  source and slide numbers, with Open project, Remove from deck and the deck-specific capability
  overview (saved through the existing editor). Other slides explain what they show and offer
  Deck settings or, for the SDD profile, Role & Charter and FFC slides, Edit slide content.
  **Deck** starts with the purpose presets, then the additional-slide cards (enable, edit,
  reorder, remove; these still save with a page reload) and every option under **All settings**.
  A preset applies its settings on the server and the page reloads once; the Deck tab then says
  "Set from <preset>", with ", adjusted" once any setting the preset decides has changed. Each
  other change saves itself in place a moment later, one save at a time; leaving the page,
  posting another form or generating waits for a pending save. The deck name and description
  are edited in the header (the pencil beside the name) and saved with the settings. A save that
  collides with someone else's change shows one banner with Reload. **Checks** counts,
  per item, how many projects lack it, and **Show** narrows the outline to them. Remove from deck
  asks for confirmation first.
- **Add projects** is a dialog with the earlier selection methods (quick add, recently completed,
  project and technical category, individual search). Each saves in place: the handler answers an
  in-page request with the refreshed deck, and plain form posts still redirect.
- The Studio remembers the selected project or slide, the view and the inspector tab for the
  session, so a page reload (settings, additional slides) returns to the same place.
- "Missing" has one rule, `ProjectBriefingContentGaps`, applied on the server to the deck's own
  settings (template, format, costs, narrative, update-sheet rows); deleted and archived projects
  are left out.
- Adding, removing or reordering projects and saving a capability overview rebuild the slides
  about a second later and keep the same project selected. Settings saves update the slides the
  same way; applying a preset reloads the page.
- Widths: three panes from 1400px (outline about 18rem, inspector about 23rem; 20rem and 26rem
  from 2200px); below 1400px the inspector moves under the slide; below 900px one column. The
  shared-deck list folds into a strip above the Studio at every width.
- Fonts follow the PowerPoint (Aptos, then Segoe UI), so line breaks can differ slightly where
  Aptos is not installed.

- Project slides use one modest corner (rounded shapes are capped at 0.12 in; PowerPoint's
  default of a sixth of the shorter side turned large panels into bubbles) and type sized for a
  room: capability points 14–18 pt by density (14 pt minimum), present status 12–14 pt, cost
  figures 15–17 pt, footer 9 pt. The capability line budget scales with the type size, so long
  overviews continue onto further slides instead of overflowing.

## Review findings (1 Nov 2026)

**Bugs**

1. A project that is in any deck cannot be purged: the deck-item foreign key restricts deletion
   and the purge never removed deck items.
2. On SDD profile and FFC footprint slides the "Data as on" text replaced the handling marking
   in the footer.
3. Deleted and archived projects stayed in decks and were still generated.
4. Moving a project across stages in the table is saved but ignored by the generator, which
   always groups by stage.
5. Project briefs and Role & Charter never continue onto a second slide; long text only shrinks.
   (Fixed in 6d.)
6. The page's slide estimate is a second copy of the generator's sequence and can disagree with it.
7. A corrupt configuration silently resets to defaults; the next save overwrites every option.

**UX**: one long scroll with Add projects last; about 15 settings and no presets; no preview;
some changes save in place and others reload the page; readiness shown three times with three
copies of the "missing" rules; gaps can be seen but not fixed on the page; two entry points for
additional slides; internal jargon ("membership", "preflight"); a thin deck list.

**Missing capability**: no stored generations or history; no "what changed since the last
briefing"; the external-status date is never shown on slides; no speaker notes; quick selections
are snapshots, not live rules; little per-project control; organisation content (SDD profile,
Role & Charter) re-entered per deck; generation runs in the request with photos loaded one by one.

## Plan: Briefing Studio

The Studio layout was approved from a clickable mock-up on 1 Nov 2026 (three panes: slide
outline, the selected slide, and an inspector with This slide, Deck and Checks tabs). Owner
decisions: no status dates on slides (the remark text alone is enough); presets with the
detailed settings under Advanced; Finalise comes with the lifecycle step.

1. **Fixes**: done (#2836). Bugs 1–3 and the empty-deck copy.
2. **Slide preview**: done (#2837). The preview is read back from the composed PowerPoint, so the
   slides and their count are exact; the page uses the full-width `studio` shell.
3. **Studio frame**: done (#2839). The three panes replace the preview section.
   - Outline built from the composer's own slide roles (Opening, Portfolio, one group per present
     stage, Closing), with a gap count on each project row and a search box.
   - The selected slide large, or all slides as a grid; arrow keys move; Present opens the
     full-window viewer.
   - Inspector, This slide: for a project, its facts, status remark, the content it is missing
     with a way to fix each item, the deck-specific capability overview edited in place, Open
     project and Remove from deck. For other slides, what the slide shows and where to change it.
   - Inspector, Deck: the current settings at a glance, opening the existing settings drawer.
   - Inspector, Checks: deck-wide counts from one rule for "missing" (`ProjectBriefingContentGaps`),
     each narrowing the outline to the projects that need it.
4. **Projects and slides in the Studio**: done (#2841). Add projects as a dialog that saves in place; the
   additional slides (SDD profile, Role & Charter, FFC footprint) managed from the Deck tab;
   drag to reorder projects within a stage in the outline (the generator's stage grouping,
   fixing bug 4); the checks section, additional-slides section, projects table and Add
   projects panel below the Studio are retired. Done before the settings step (owner, 1 Nov
   2026) so in-place settings never leave older sections stale.
5. **Deck settings in the inspector**: done (#2844). Purpose presets at the top of the Deck tab (Monthly
   command review: standard, combined, capability overview, both costs, stage summary,
   Editorial Light, Jai Hind; Visit or external audience: detailed slides with photos, no
   costs, SDD profile, Thank You; Project update sheets: one sheet per project with the
   recommended rows), the chosen preset remembered ("adjusted" after changes), every option
   under Advanced, each change saved in place with one conflict banner, the deck name and
   description edited in the header; the settings drawer and summary bar are retired, with
   their CSS. The preset is stored as an additive `preset` key in the deck configuration, which
   older builds ignore; one server-side definition (`ProjectBriefingDeckPresets`) both applies a
   preset and decides "adjusted".
6. **Per-project control**, in small PRs, each stored per deck item:
   - 6a, done (#2846): how the project appears, chosen in This slide: **Own slides**,
     **Table row only** (a row in the status table with no slides of its own; decks without a
     status table list these projects in an "Other projects in this briefing" table after the
     project slides or update sheets) or **Leave out** (kept in the deck, not in the PowerPoint,
     listed last in the outline under "Not in the PowerPoint"). Checks, counts, photos and the
     slide estimate follow the choice. Column `ProjectBriefingDeckItems.Presentation` (default
     `Slides`, so existing decks are unchanged).
   - 6b, done (#2847): the photograph a deck shows for a project. This slide's
     **Photograph** section (for projects with their own slides in a format that shows photos)
     opens the project's photos; choosing one stores it on the deck item
     (`ProjectBriefingDeckItems.PhotoId`), choosing the cover clears it so the deck follows the
     project's cover. Deleting the photo clears the choice (foreign key, set null); a stored
     photo that no longer belongs to the project is ignored.
   - 6c, done (this change): speaker notes. This slide's **Speaker notes** box (for projects
     with their own slides) saves when it loses focus or with Save notes
     (`ProjectBriefingDeckItems.SpeakerNotes`, up to 4,000 characters). The composer writes them
     into the PowerPoint's notes for the project's first slide only, adding a notes master
     when the deck has notes (the template has none); the integrity check validates the notes
     pages and master. The slide preview is unchanged.
   - 6d, done (this change): continuation slides (fixes 5). **Role & Charter** stays one slide
     while its charter fits at 10 pt or larger (`ProjectBriefingRoleCharterPaginator`); a longer
     charter keeps the role and as many items as fit on the first slide and continues, in source
     order, on "— Continued" slides, never an empty one. The paginator also sizes the charter type
     for the renderer, with a line estimate that scales with font size, and role statements now
     size to fit their panel instead of a fixed 16 pt. A **project brief** that fits beside the
     photograph keeps its typography; a longer one keeps the photograph, the start of the brief and
     the status/cost strip, and continues at 12 pt or more on full-width "Project brief — continued"
     slides (`ProjectBriefContinuation` role), split at paragraph or sentence boundaries by the
     update sheets' brief splitter. The slide estimate counts both. Owner decisions (4 Nov 2026):
     continue Role & Charter below 10 pt, reversing the August single-slide rule; briefs as photo
     slide plus full-width text.
7. **Library and lifecycle**: the library is done (#2881: table with purpose, owner, slides, last
   edited and generated, search, Mine/All, sort; #2882 and #2886: "data moved since last
   generated"). The rest is **parked** (owner, 5 Nov 2026): Draft/Finalised/Presented with the
   PowerPoint kept on the LAN store, pin and archive, a "What changed since the last briefing"
   slide, and organisation content (SDD profile, Role & Charter) kept once. Nobody needs to look
   back at exactly what was briefed, so a kept record has no user yet. Revisit if old briefings
   start being queried or the number of decks grows.

### Wide screens

The Studio must use wide monitors, not just fit them:

- The page uses the `studio` shell (done with the preview): page and top bar take the full window, less the desk gutter, instead of the 1840px desk cap.
- The Studio opens with the workspace navigation folded to its icon rail; expanding it is
  remembered for the Studio alone (`prism.workspace.navigationExpanded.studio`), apart from the
  other workspace pages.
- The panes follow the Studio's own width (`.pbd-studio-frame` is the `pbd-studio` size
  container), not the window, so an open navigation narrows them correctly:
  - **≥ 2050px**: outline 20rem, preview, inspector 26rem.
  - **1400–2049px**: outline 18rem, preview, inspector 23rem.
  - **1100–1399px**: outline 13.5rem, preview, inspector 20rem (laptops: 1366px gives a 633px
    preview, 1440px 705px).
  - **940–1099px**: outline 13rem, preview, inspector 19rem.
  - **700–939px**: outline and preview; the inspector moves beneath.
  - **< 700px**: one column.
- Segmented controls keep whole labels: an option grows to fit its label rather than showing an
  ellipsis.
- Not built: a filmstrip or two slides side by side on very wide screens, and the inspector as a
  drawer.
- The deck library is a table; it scrolls sideways inside its card on narrow screens.

## Plan: Project key points

Agreed with the owner on 5 Nov 2026 from the Design canvas "Project slide key points mockup"
(today's brief slide, a first layout, and the approved Option B with the status remark off and
on).

**What they are.** Five short points that say why a project matters, separate from the
capability overview (what the system does) and the project brief (the narrative):

| Heading | Answers |
|---|---|
| Training gap | What cannot be trained today |
| Op necessity | Why the product is operationally required |
| Training realism | How close it is to the real thing |
| Ease of learning | How quickly a trainee picks it up |
| Proliferation potential | Who else could use it |

Owner decisions (5 Nov 2026):

- The headings are fixed: names and order are the same for every project and deck, defined in one
  place. A wording change applies everywhere; a heading's meaning never changes. No per-project or
  per-deck headings. A second heading set by technical category is a later option, only if
  non-training projects need one.
- Each point is optional, plain text, one or two lines on the slide: at most 130 characters, no
  line breaks, enforced on the server. A project shows only the points it has; none, no band.
- Points live on the project (written once, beside the project brief on the Project Overview) and
  every deck reuses them. The project brief is not capped or shortened: the Brochure and
  Compendium use it too, and the full brief fits in Option B.
- A deck switch, **Show key points**, off by default. With Project content "Both" the author picks
  the capability slide or the project brief slide; otherwise the points go on the only project
  slide. Update sheets are unchanged.
- **Option B** is used only by decks with key points switched on, for every project in the deck
  (a project without points has no band). Existing decks generate exactly as before.

**Option B layout** (both project slide kinds):

- Stage and cost on one line under the title ("Development | Cost (R&D) ₹4.20 Cr AoN |
  Proliferation ₹1.10 Cr"); the cost cards and context card go. Hidden stage or cost drops out
  of the line.
- Top row: the photograph at 16:9 and the brief or capability text, the same height.
- A full-width band at the bottom: the present status remark as its first row when "Show present
  status" is on, then the key points (heading column about 2 in, text beside it).
- Whatever is switched off gives its space to the top row. A brief that still does not fit
  continues on "Project brief — continued" slides as today.

**PRs**

1. Key points on the project: done (this change). Table `ProjectKeyPoints` (project, kind, text;
   one row per recorded point, additive migration), headings and hints in
   `ProjectKeyPointCatalog`, a **Key points** tab in Project content after Capability overview,
   written by Admin, HoD and the project's own Project Officer (as the brief). Each field shows a
   hint and a character count; Enter is ignored and line breaks become spaces; the server
   collapses whitespace and rejects a point over 130 characters.
2. Option B on the capability and project brief slides, with the deck switch and the slide
   choice: done (this change). The Deck tab's **Key points** setting (`keyPoints` in the deck
   configuration's `standardBriefing`; older builds ignore it) shows "On the project brief slide"
   or "On the capability slide" only when the deck draws both. `ProjectBriefingSlideComposer.KeyPoints`
   draws both slide kinds: stage and cost on one line under the title (the capability slide keeps
   its lifecycle and category line when neither is shown); a 16:9 photograph beside the text,
   which narrows (5.7, 5.0, then 4.4 in) before the text continues on another slide; the band
   with the status remark and, on the chosen slide, the recorded key points. The capability and
   brief pagination follow the smaller text panel; the preview reads the composed PowerPoint, so
   it is exact. The Checks tab lists projects with no key points as "Key points" (a project with
   some is complete). The slide estimate still assumes the established layouts.
3. "Data since": done (this change). A generation that drew key points keeps a digest of them
   (`ProjectBriefingShownFacts.KeyPoints`, fact key `k`; facts kept earlier have none and never
   count). Adding, editing or removing a point then shows on the library as "key points edited
   on N projects" and, per project, "key points". The lifecycle subtitle is tracked as the key
   points layout draws it: on a first capability slide only when it has no stage and cost line.

## Plan: Deck settings

Agreed with the owner on 5 Nov 2026 from the Design canvas "Deck settings redesign" (a clickable
Deck tab).

**Review findings.** The Deck tab is a 366 px column about 3,100 px tall: 43 controls under 21
headings, every choice a large card stacked one per row. Groups follow the code, not the decision
(cost under "Financial information" near the bottom; "Summary and handling" mixes summary slides
with the marking). Additional slides appear twice (a "2 enabled · Manage slides" line in the
settings and the full list below them). Options that do nothing stay visible (Project brief layout
while key points are on). There is no overview of how a deck is set up without opening everything.
A separate icon rail was considered and rejected: the inspector is narrow and has few steps.

**Owner decisions (5 Nov 2026).**

- The purpose presets are called **Template**, shown as one compact picker at the top; after
  any change it reads "customised" ("Started from this template, then customised"). Superseded
  on 6 Nov: see "Templates are slide designs".
- The Standard briefing / Update sheets choice is called **Layout**, inside the Slides step
  (now chosen through the Template picker; the control is hidden).
- Settings become numbered steps, one open at a time, each closed step showing a one-line summary
  of its choices. Steps that do not apply are not shown:

| Step | Holds | Shown when |
|---|---|---|
| Slides | Layout; what the deck shows (Table / Project slides / Both); what each project gets (Capability / Brief / Both) | always (the last two for the standard layout; the last only with project slides) |
| Project slides | present stage and status remark (chips); key points (switch, and the slide when the deck draws both); project brief layout (only while key points are off and briefs are drawn) | standard layout with project slides |
| Sheet rows | rows to show, in order; hide rows with nothing recorded | update sheets |
| Cost | Cost (R&D) / Proliferation / Both / None | standard layout |
| Extra slides | summary slides (stage-wise, with its distribution table; category summaries) or, for update sheets, cover and portfolio summary; institution slides (SDD profile, Role & Charter, FFC footprint) with a switch and Edit each | always |
| Look | theme, insignia, closing slide | always |
| Handling | classification marking | always |

- Compact controls: segmented buttons, chips and switches replace the choice cards; one line of
  help for the selected option only.
- Changes keep saving as they are made; the form fields and the server stay as they are.

**PRs**

1. Quick fixes: done (this change). The "N enabled · Manage slides" summary inside the settings is
   gone, so additional slides live only in the Deck tab's Additional slides list; Project brief
   layout is hidden while key points are on (on load and as the switch changes).
2. The step list: done (this change). The Template picker shows the current template and its
   summary, or "Started from this template, then customised." with a "customised" badge; the
   three templates open beneath it. "All settings" is gone: the settings are six numbered steps
   (`data-pbd-step`: slides, project, sheet, cost, extra, look, handling; Project slides only with
   project slides, Sheet rows only for update sheets, Cost only for the standard layout), closed
   by default, one open at a time (remembered per layout), each summary rebuilt from the form on
   every change, numbers following the visible steps. The existing controls moved into their steps
   with shorter wording; the field names and saving are unchanged. The library's column is
   "Template".
3. Compact controls: done (this change). Every choice card in the steps is a segmented control
   (`pbd-seg`, a 2×2 grid for cost) whose one help line (`data-pbd-seg-help`, from each option's
   `data-help`) explains only the selected option; stage and status are chips; key points is a
   switch; the themes are two small swatches. The radios and checkboxes stay real, focusable
   inputs with the same names. The open step stays open when other settings change (the
   remembered step is restored only when the layout changes).
4. Extra slides: done (this change). The Extra slides step holds the summary slides (each note on
   its own line) and the institution slides, one compact row each: drag handle (a pin for the FFC
   footprint, fixed before closing), a switch with the name and position, Edit (the existing
   editors) and remove (asks first). "Add slide" shows only while a slide type is missing. The
   rows' toggle, remove and order forms sit outside the settings form (forms cannot nest); the
   switch and remove button reach theirs through the `form` attribute. The separate Additional
   slides section and its card styles are gone; the step's summary lists the enabled institution
   slides. The Deck tab is about 700 px tall with every step closed (it was about 3,100 px).
5. Preview follows the change: done (this change). A successful settings save names the settings
   that changed (`pbd:settings-saved`); after the rebuild the Studio shows the first slide of the
   roles the latest change affects (`SETTING_SLIDES`: a project setting shows the project in view
   when it has that slide, else the first; the key points switch and slide follow the chosen slide;
   stage and category summaries, cover, portfolio summary and closing show their slide). Theme
   and marking keep the slide in view. Every rebuild now also keeps the kind of slide in view for
   its project (a brief stays a brief), not only the project.

## Plan: Studio usability

A walkthrough on 5 Nov 2026 as a first-time user (new deck, 49 ongoing projects, template switch,
checks, Present, phone width) on a copy of the development database.

**Review findings.**

- **The preview is the smallest pane on a laptop.** The breakpoints use the window width, but the
  workspace navigation takes about 250 px of it. At 1440 px the panes are navigation 252, outline
  286, preview 389 and inspector about 380 px; the slide is about 360 px wide and its text cannot be
  read. Below 1400 px the inspector drops beneath the outline and preview, out of sight. At 1920 px
  the preview is about 830 px and works. The "Wide screens" plan above (inspector as a drawer at
  1200–1599 px) was never built.
- **A new deck has no workflow.** New deck asks only for a name; the deck opens as "Set up by hand"
  with "Add projects to build the slides." three times, and the outline says "Slides 4" while it
  is empty. Nothing shows the order: template, projects, gaps, settings, generate.
- **Templates overwrite without warning.** Picking a template applies it at once, with no
  confirmation and no undo, even when the deck was customised.
- **"Present" is a viewer.** It opens an in-page dialog with a filmstrip; the slide fills about
  60 % of the window, it is not full screen, and speaker notes ("shown to the presenter") only reach
  the PowerPoint.
- **The model is not explained.** The deck rebuilds from live PRISM data, saves as you go and is
  shared with every Comdt/HoD user; none of this is visible where it matters. Gap fixes either
  change the project record ("Record in project", "Write in project") or only this deck ("Write for
  this deck"), with no label saying which.
- **Scope and signals.** Selecting a slide leaves the inspector on the Deck tab. A gap shows twice
  (the card under the preview and the This slide tab). The outline's orange numbers (missing
  items) have no explanation. In Add projects the button still reads "Add" after adding, and
  Escape did not close the dialog. The library's Slides column shows "—" until a PowerPoint is
  generated, though the Studio knows the count. "New briefing deck created." stays on screen; "Missing content uses defined
  placeholders." sits under Generate on an empty deck; the browser title says "Project Briefing
  Deck Builder" while the page says "Briefing decks".
- **Phone.** The Studio is about 11,800 px tall: the All slides grid (remembered from the desktop)
  is about 9,600 px, and the settings start near 11,000 px.

What works: the template descriptions, the numbered settings steps, the Checks tab ("8 of 49
projects missing", Show) and the preview following each settings change.

**PRs**

1. **Room for the preview.** Done (this change). The Studio opens with the workspace navigation
   folded (its own remembered choice) and the panes follow the Studio's width through container
   queries; see "Wide screens". The preview pane is 633 px at 1366 px with the inspector beside it (before, the inspector sat beneath, out of sight), 705 px at 1440 px
   (was 389) and 1,034 px at 1920 px (was about 830); the inspector stays beside it down to about
   1100 px. The outline's buttons move under its title instead of breaking, and segmented labels
   are never cut off in the narrower inspector. The drawer was not needed.
2. **First run.** Done (this change). New deck asks for the name and the template (the three
   templates, Monthly command review selected, and "Set up by hand"); `CreateAsync` applies the
   template the same way as the Studio does, and "Create and add projects" opens the Studio on
   Add projects (`?start=projects`, removed from the address once used). With no projects the
   preview shows one empty state ("Add the projects to brief", one sentence, one Add projects
   button), the outline says "No slides yet." and shows no count, and the status line is empty.
   Picking a template on a customised deck asks first ("Apply …?", Keep my settings). After any
   template the page shows "Template applied: … · Undo": `ApplyPresetAsync` returns the settings
   it replaced (`ProjectBriefingPresetUndo`: format, slides, costs, look, sheet rows, closing, the
   SDD profile switch and modules, extra-slide order, the template), kept in TempData (protected by
   data protection) and offered only while the deck is still at the version the template left;
   `UndoPresetAsync` refuses otherwise, and any other change in the Studio removes the Undo bar.
   Success messages fade after six seconds; the placeholder note under Generate shows only while a
   check is open; the browser title is "Briefing decks" (or the deck's name in the Studio).
3. **Guidance in the Studio.** Done (this change). Under the deck header a numbered progress line
   reads Projects (count, or Add) · Checks (n to review, or All recorded) · Settings (template
   name, ", customised") · Generate; Projects opens Add projects, Checks and Settings open their
   inspector tabs, Generate goes to the button. The Studio updates the figures after every
   rebuild. A note says once how the deck works (rebuilt from live PRISM data; saves as you go;
   shared with every Comdt and HoD workspace user) until "Got it" (remembered per browser), and
   "Saved · shared" by the title turns to "Saving…" or "Not saved" while in-place changes run.
   The gaps moved from the card under the preview into This slide ("Missing · n", at the top),
   so each shows once; the Present status and Capability overview sections are left out there
   while their gap is listed. Each fix says where it lands: "Fix in project" (opens the project;
   every deck and page shows the change) or "This deck only" (text kept with the deck). Picking
   a slide in the outline or the All slides grid opens This slide. The outline's orange number
   explains itself on hover and to screen readers. In Add projects a quick add turns to
   "Added" (fresh again each time the dialog opens), focus returns to the button after saving,
   and Escape closes the dialog even when focus has left it. The library's Slides column keeps
   "—" until the first PowerPoint, now with a tooltip: the preview count is not stored, and
   storing it would need a new column for little gain.
4. **Present.** Done (this change). Present opens the page full screen (Fullscreen API, from the
   Present click); where the browser refuses, the dialog still fills the window. Leaving full
   screen (Esc) ends the presentation and the Studio returns to the slide it ended on. The slide
   is as large as the screen allows; the strip of every slide ("Slides") and the speaker notes
   ("Notes", or N) open on request, and both choices are remembered on the browser. The notes
   are the project's saved speaker notes, shown on each of its slides (the PowerPoint carries
   them on the first); other slides say they have none. It starts at the slide in view, with
   "From the start" in the toolbar beside Notes and Slides. The header lists the keys (arrows,
   N, Esc); Space, Page Up/Down, Home and End also move.
5. **Phone.** Done (this change). When the Studio is narrower than 700 px the slide comes first,
   with Present in its bar (Generate stays at the top of the deck header); the outline follows as
   a sheet: its header (Slides 52, Show slides, + Projects, + Slide) with the list folded until
   "Show slides" (up to 60 % of the screen, scrolling). Picking a slide folds the sheet and
   brings the slide into view; Show in Checks opens it. The inspector comes last, in the page
   flow rather than sticky. A remembered All slides view is not restored below 900 px. At
   375 px the page went from about 2,800 px to 1,640 px, and the slide starts at about 740 px
   instead of 1,490 px. The settings stay in the Deck tab under the slide rather than a
   separate sheet: with the outline folded they are a tab away.

## Templates are slide designs (6 Nov 2026)

Owner decision: a template is a slide design; anything else is a setting the user can change.

- **Two templates.** **Portfolio** (a status table and stage summary, then a capability slide
  with photograph per project) and **Fact Sheets** (one formal sheet per project: the
  recommended facts, brief and photograph). They were "Monthly command review" and "Project
  update sheets"; the names are labels in `ProjectBriefingDeckPresets`, so no data changed.
- **Visit or external audience is retired.** It was settings (no costs or status, SDD profile,
  Thank You), not a design, and is no longer offered or accepted by `ApplyPresetAsync` /
  `CreateAsync`. The enum value stays so stored decks still read; such decks keep every setting
  and show as Portfolio, customised. No reminder about costs for visitors (owner).
- **Template replaces Layout.** The Template picker is the one way to choose the design; the
  Layout control in the Slides step is hidden (its inputs stay for the form and the step
  summaries). Applying a template still asks first on a customised deck and offers Undo.
- **Every deck shows a template.** `TemplateOf` gives the deck's own template while it is still
  offered and matches its design, otherwise the template for its design; "Set up by hand" is
  gone from New deck, the picker and the library. A deck whose settings differ from its
  template's reads "customised".

## Walkthrough fixes (6 Nov 2026)

A first-time-user walkthrough on master at 1366×768 (after the Studio usability plan and the
template change) found:

1. **"Added" unreadable.** After a quick add the focused button took Bootstrap's blue background
   under green text. Added now keeps its green look in every state.
2. **Header icon gone in dark theme.** The tile turns light there while the icon stayed white; the
   icon now uses the surface colour, like the library's.
3. **The slide started too low on laptops** (about 480 px down; a 1366×768 browser shows about
   650 px). The Studio's own row above the header is gone: "← Briefing decks" sits in the header
   where the "Briefing deck" eyebrow was, Switch deck beside Generate, New deck in the ⋯ menu.
   The line under the slide is one line ("62 slides · built … from current PRISM data", the
   promise about Generate on hover).
4. **Fixes did not show on return.** Coming back to the Studio tab (or window) rebuilds the slides
   quietly: at most every 15 seconds, never while a build runs, while presenting or while typing.
5. **"Fix in project" for a status remark** opens the project at its remarks section (`#remarks`,
   which the project page already opens). The cost gaps still open the project page; it has no
   cost section to land on.

Then (follow-up):

6. **Library rows stay one line at 1366 px.** Table dates drop this year's year ("05 Nov", "Today,
   06:44"; other years "05 Nov 25") with the full date on hover; the owner truncates with the
   full name on hover; "Data since" shows a dash before the first PowerPoint instead of repeating
   "Not generated yet".
7. **Loose ends.** The "… projects added" confirmation under the outline clears after six seconds
   (errors stay), and Add projects has a **Done** button (adding saves at once; Done only closes).

## Independent review (6 Nov 2026)

A cold UI/UX review by a separate reviewer (no earlier findings shared), on master at 1366×768,
1920×1080 and 375×812, light and dark. Confirmed by hand: the Add projects tab strip collapsed
to 9 px after a search at 1366×768, and the Studio's arrow keys only work while focus is inside
the Studio.

**Plan, by PR**

- **A. Add projects.** Done (this change). The dialog body's rows take their content's height,
  so the tab strip (a sideways scroller) no longer shrinks; one scroll (the dialog's), the result
  list growing with it and the Apply bar sticky at the bottom. Quick and category adds offer
  **Undo** (removes exactly the projects that add brought in). Manage individually opens on the
  deck's own projects (untick to remove, search to add); badges read **In deck** (saved),
  **Will be added** / **Will be removed** (pending until Apply), nothing for a project outside the
  deck; "Completed · Completed" is said once. The dialog's lead says which parts save at once.
- **B. Studio fits the window.** Done (this change). The progress line moved into the deck
  header, beside the last change, so the Studio starts about 100 px higher (slide top 317 px at
  1366×768, was 358 after the one-time note). The side panels take the window's height below
  the header (`--pbd-studio-offset`, measured by the Studio script and kept current as the header,
  the note or the window change; at least 26rem), so the whole Studio fits 1366×768 and
  1920×1080; only the site footer is below. Outline titles show two lines. Left/right arrows move
  between slides from anywhere on the page (after clicking the slide focus is on the page), not
  from fields, tabs, menus, dialogs or drawers. After a reload the outline scrolls itself (not the
  page) to the slide the Studio reopened on, and the Checks filter comes back with the place.
- **C. Gaps made honest.** Done (this change). Proliferation cost is a gap only for a completed
  project (owner, 6 Nov 2026; `ProjectBriefingContentGaps`, so the server count and the Studio
  agree). The slides follow the same rule (`ShowsProliferationCost`): an ongoing project with no
  proliferation cost has no proliferation card or key-fact entry, and a dash in the status
  table's column; a recorded cost always shows, and a completed project without one still says
  "Not recorded". The inspector's facts do the same. One word, **gaps**: the inspector tab and the progress step are "Gaps" ("5 to fill" /
  "None"), This slide "Gaps · n", the outline badge "⚠ 3" with "3 gaps: …" on hover and to screen
  readers, the note under Generate "Gaps show as placeholders on the slides.", and each Show
  button is named for its gap. This slide shows each field once (a cost listed under Gaps is not
  repeated in the facts). Present and Generate first say "Present/Generate with gaps?" (n kinds
  still open, placeholders such as "Not recorded"; *Present anyway* / *Review gaps*, which opens
  the Gaps tab), once per deck per browser session.
- **D. Phone.** Done (this change). Below 576 px the Studio header gives Generate its own
  full-width row (Switch deck and the deck menu beneath); the deck icon, the "Updated" line and
  the note under Generate give way (the Gaps step says it), so the progress line takes the full
  width. The header went from 384 px to 237 px tall at 375 px. The library table becomes a list of
  cards: the deck name on top with its ⋯ menu, then each value with its label (Template,
  Projects, Slides, Owner, Edited, Generated, Changed since; `data-label` on each cell); no
  sideways scroll. Desktop is unchanged.
- **E. Wording and accessibility.** Done (this change). The progress line is three steps
  (Projects, Gaps, Settings) with dots instead of numbers, so only the Deck tab's settings steps
  are numbered (the owner's numbered steps stay); Generate is the button beside it. The deck name
  is the Studio's `<h1>`. The SDD profile, Role & Charter and FFC drawers speak plainly ("The
  figures come from PRISM and update by themselves", "Turn it on or off, and set its place, in the
  Deck tab under Extra slides", "A large map with the country list beside it"), and Escape closes
  each even when focus is outside it. The note under Generate is 12 px. Library: "Data since" is
  "Changed since" (with a tooltip), and a search that found a deck through its projects says
  which ("Matches: …"). New deck states its rule ("required, at least 3 characters"). Update-sheet
  row checkboxes are named. Left as they are: the stage names in the outline (PRISM's own stage
  names), the Template picker showing the current template above its list, and the rail's
  contrast (a workspace-wide change).

## Library review (6 Nov 2026)

An independent UI/UX review of the deck library (`/Workspace/BriefingDecks`) at 1366×768,
1920×1080 and 375×812, light and dark. Plan, by PR:

1. **Continue and Generate.** Done (#2950). The card is "Continue" only for the deck this
   person last edited; otherwise it is "Recently edited" (the deck most recently edited by anyone;
   decks are shared). Generate in the library (the card and each deck's ⋯ menu) no longer posts
   straight to the server, which gave no progress, no double-click guard, no gap question and a
   stale row afterwards: it opens the deck with `?generate=1`, and the Studio runs its own
   Generate (saving pending changes, "Generate with gaps?", progress, the result line); the flag
   is dropped from the address. A deck with no projects offers **Add projects** (opening the
   Studio on Add projects) instead of a greyed-out Generate. The server's Generate handler stays
   for compatibility.
2. **The table.** Done (this change). Five columns: Deck (the widest), Template, Projects,
   **Edited** (when, and who below; the creator in the tooltip) and one **PowerPoint** status:
   "Not generated" (or "No projects yet"), else "05 Nov · 54 slides" with "up to date", "3 changes
   since" (linking to the deck, the tooltip saying what moved) or "changes not tracked yet". The
   heading reads "My decks" under Mine; an empty list says why ("You have not created or edited a
   deck yet…" or "No deck matches …") with **Show all decks** / **Clear search**. Search, Mine/All
   and sort are kept for the browser tab. The library and its header stop widening at 84rem.
3. **Polish.** Phone cards and the Continue card shorter; New deck hints at 12 px with an inline
   error and a fresh form each time; one `<main>`; the shared focus ring on deck links; the library without the
   Studio's scripts.

Outside this module: the shared "What's new" announcement can cover the page's top-right buttons
on a first visit.

