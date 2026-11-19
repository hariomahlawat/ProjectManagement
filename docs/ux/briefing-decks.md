# Briefing decks (`/Workspace/BriefingDecks`)

Comdt and HoD users build shared PowerPoint briefing decks from live PRISM data; Project Officers
build decks of their own. A deck keeps its projects, their order and its settings; every
PowerPoint is built from current PRISM data at the moment it is downloaded.

This file describes the module as it is now. How it got here (plans, reviews and owner decisions,
1 Nov to 19 Nov 2026) is in [briefing-decks-history.md](briefing-decks-history.md); see
[History](#history) below.

## The quick path

The Studio is optional (owner, 19 Nov 2026). Making and downloading a deck takes one dialog:

1. **Briefing decks → New deck**: a name, a slide style, and the projects (ticked with filters, or
   **Same as a deck** to copy another deck's list).
2. **Create and download**: if some projects lack content their slides show, the page asks first
   ("Download with gaps?"); then the PowerPoint downloads and the library shows the new deck.

Later, **Download** on the deck's card makes a fresh PowerPoint from today's data, asking about
gaps the same way. **Open** (or **Create and open**) goes to the Studio, which is for reviewing
slides, filling gaps, reordering and per-deck choices.

## Where it lives

| Concern | Code |
| --- | --- |
| Page | `Pages/Workspace/BriefingDecks/Index.cshtml(.cs)`; library `_DeckLibrary`, `_DeletedDecks`; design drawings `_DesignMini`; editors `_InstitutionalProfileEditor`, `_RoleCharterEditor`, `_FfcGlobalFootprintEditor` |
| Deck persistence | `Services/ProjectBriefings/ProjectBriefingDeckService.cs` (create with projects and style, settings, blocks, membership, delete and restore); options in `SelectionRulesJson` via `ProjectBriefingDeckConfigurationCodec` |
| Slide styles | `ProjectBriefingProjectSlideDesigns` (a style is read from and written as the deck's layout, narrative and key point settings) |
| Page data, gaps | `Services/ProjectBriefings/ProjectBriefingDataService.cs`; "missing" has one rule, `ProjectBriefingContentGaps` (also `Summarize` for the library's question) |
| Slide generation | `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer*.cs`, `ProjectBriefingPowerPointExportService.cs` (`ComposeAsync` builds the deck; `GenerateAsync` also records the generation; `DownloadAsync` does not), `ProjectBriefingPresentationIntegrityValidator.cs` |
| Studio preview | `ProjectBriefingSlidePreviewRenderer.cs` (composed PowerPoint → positioned HTML); slide roles from `ProjectBriefingSlideComposer.ComposeWithOutline`; handler `OnGetPreviewAsync` |
| Scripts | `wwwroot/js/pages/project-briefing-library.js` (library only), `project-briefing-decks.js` (shared: settings, New deck, Download in the Studio), `project-briefing-studio.js`, `project-briefing-add-projects.js` (Add projects and the New deck picker), `project-briefing-additional-slides.js`, `project-briefing-focus.js`, `project-briefing-confirm.js` |
| Styles | `wwwroot/css/pages/project-briefing-decks.css` |
| Access | `Policies.ProjectBriefingDecks.Manage` (Comdt, HoD, Project Officer); each deck by `ProjectBriefingDeckAccessRules` (`ProjectBriefingDeckAccess.cs`): command decks shared by Comdt and HoD; a Project Officer's personal deck changed only by its owner, downloadable or duplicable by command users |
| Tests | `ProjectManagement.Tests/ProjectBriefings/*`; `wwwroot/js/projects/project-briefing-*.test.js`; `wwwroot/js/pages/project-briefing-add-projects.test.js` |

## Deck library (landing page)

`/Workspace/BriefingDecks` without a deck id is the library; it never opens a deck the person did
not pick.

- **Deck cards**, four across at 1920 px, three on a smaller wide screen, two on a tablet, one on a phone. Each shows a
  drawing of the deck's slide style, the name and description, the style, the project count, who
  edited it last and when, and one PowerPoint line: when it was last generated and how many
  slides, then "up to date", "N changes since" (a link to the deck) or "changes not tracked yet";
  "Not generated" or "No PowerPoint yet" before that. The deck the person last edited is tagged
  **Continue** (else "Recently edited" on the latest) and is first with the default sort (last
  edited), even when someone edited another deck since.
- **Buttons**: **Download** and **Open**; an empty deck offers **Add projects** and Open. The ⋯
  menu has Duplicate and Delete (through the confirmation dialog).
- **Download** makes the PowerPoint without opening the Studio. It reads the deck's gaps
  (`OnGetGapsAsync`) and, when a presented project lacks content its slides show, asks "Download
  with gaps?" (*Download anyway* / *Cancel*); then it posts `Generate` in the page, so the
  generation is recorded and the card's line becomes "Today, HH:MM · N slides · up to date". The
  button stays busy until the file arrives; an expired sign-in or a refusal is said in a toast.
- **A Project Officer's deck** seen by a command user is tagged "<name>'s deck · view only" and
  offers Download (asking about gaps the same way; nothing recorded) and Duplicate.
- **Mine / All**, search (which also matches the names of projects in a deck) and sort (last
  edited, last generated, name) run in the page and are kept for the browser tab.
- **Needs attention**: up to three decks whose data moved since they were generated, with the
  projects that moved (below).
- **Recently deleted** below the cards: a deleted deck can be restored for `DeletedRetentionDays`
  (30). After that the next delete removes it for good, as its own step after that delete's save,
  in its own transaction with one required `ProjectBriefing.DeckPurged` audit entry per deck; if
  any entry cannot be written nothing is removed, and a failed purge is retried on the next
  delete (it never undoes the delete). Deleting a deck shows a toast with **Undo**, which restores
  it at once (from the library and from the Studio).
- An unknown or deleted deck id returns to the library with a one-request notice. An old
  `?generate=1` link only opens the deck.
- The shared What's new card waits on these pages (`data-whats-new-wait`), so it never covers New
  deck or Download.

### Data moved since last generated

Each generation keeps, per presented project, the facts its slides drew and which ones
(`LastGeneratedFactsJson`, `ProjectBriefingSlideFacts`): the stage as the slides read it (its name
where a table row, detail slide or the stage-wise distribution shows it; its maturity where it
orders projects; its lifecycle for a project slide's subtitle and the distribution; the milestone
as `ProjectBriefingUpdateSheetPlanner.DescribeMilestone` draws it; the distribution's band), the
external status remark (date and a digest of its text), the key points where a slide shows them,
and the costs drawn (R&D with its basis label, and proliferation by cost mode).
`ProjectBriefingSlideFacts.Shown` mirrors the composer, so a fact a deck hides, or a settings
change since, never counts. The library rebuilds today's facts with `GetCurrentSlideFactsAsync`
(the same cost, status and stage rules as generation) and compares only what each project's slides
showed. Deleted or archived projects are left out, as generation leaves them out. Content gaps are
not counted on the library: that would mean composing every deck on each visit.

## New deck

The dialog (also in the Studio's ⋯ menu) asks for:

- **Name** (required, at least 3 characters) and an optional description.
- **Slide style**, one for every project, with the Deck tab's drawings and names: Capability, Key
  points, Brief, Fact sheet, Key points only. The server starts from the template for the style's
  layout and sets the style (`SetDesign`), as the Deck tab would. "Capability and brief" is kept
  for decks that already use it and refused for a new deck. A form from an older page that posts
  `Template` (Portfolio or Fact Sheets) still works.
- **Projects**, optional: **Choose projects** (the Add projects filters in picker mode: deck 0,
  nothing saved until Create; Enter in its search searches and never submits the form) or **Same as
  a deck** (any deck the person can see; its project list is copied as it is today, in its order,
  not its notes, photographs or settings). The deck and its projects are one save; deleted,
  archived and cancelled projects are left out, and a list with none left, more than 120, or from
  a deck the person cannot see is refused with no deck made.

The footer says how many projects are chosen. **Create and open** posts the form and opens the
Studio (on Add projects when none were chosen). **Create and download**, on the library only,
creates the deck in the page and hands it to the library's Download, then shows the library with
the new deck on top and says how it ended: downloaded (with the slide count), created but not
downloaded (the person chose Cancel on the gaps question), or created but the PowerPoint could not
be made (the reason, in a toast that stays until closed). The options of **Same as a deck** name
each deck's project count and owner ("yours" for the person's own). Marking is not asked for (owner, 19 Nov 2026); it stays optional in the
Studio.

## Studio

`/Workspace/BriefingDecks/{id}`. The server composes the same PowerPoint that **Download
PowerPoint** gives (without recording a generation) and reads it back into positioned HTML:
shapes, text, lines, pictures, tables and groups, in the composer's own colours, so the slides and
their count are exact. The composer reports what each slide is (`ProjectBriefingSlideRole`, plus the
project for project slides), which the outline uses instead of reading slide text. Pictures are
shrunk to 960px and sent once each. Every text value is encoded on the server; the script inserts
only slide HTML and writes all other text as text.

- **Header**: "← Briefing decks", the deck name (the pencil renames it and edits the description)
  with its saved chip ("Saved · shared"), one line (project count, slide style, last change), then
  **Download PowerPoint**, **Focus**, **Switch deck** and the ⋯ menu (New deck, Duplicate,
  Delete). Download saves pending changes first, then asks "Download with gaps?" once per deck per
  browser session when gaps are open.
- **Outline** (left): Opening, Overview, one group per present stage (in the deck's maturity
  order), then Closing. Overview slides are named as the Deck tab names them ("Stage-wise
  distribution", "Stage-wise distribution, project list", "Status table"), with the slide's own
  heading on hover. A project's slides share one row; runs of status-table, category and FFC pages
  fold into one row. Project rows carry an amber count of their gaps. Search filters by title,
  stage and category. Drag a project row by its grip, or Alt+Up / Alt+Down, to move it within its
  stage (the generator groups projects by present stage). **+ Projects** opens Add projects;
  **+ Slide** opens the slide library.
- **Slide** (centre): the selected slide as large as the pane allows, or **All slides** as a grid.
  Arrow keys and buttons move between slides; **Present** opens the full-window viewer (strip and
  speaker notes on request; Page Up/Down, Home, End, Escape) and returns to the slide it ended on.
  The refresh button rebuilds from current data.
- **Inspector** (right), three tabs:
  - **This slide**: for a project, what its slides show, each fact once: status remark, costs,
    capability source, slide numbers; a fact the project lacks shows in amber ("Not recorded")
    with its fix beside it (*Fix in project*, or *This deck only* for capability text), then Open
    project. *Fix in project* is offered only to those who may change the project (Admin, HoD, or
    its lead Project Officer: `ProjectAccessGuard.CanManageProjectContent`); others read "Ask the
    project's officer or an HoD to add it." Slide numbers read "Slide 5" or "Slides 5, 6". **More options** (closed by default, open or closed for the session) holds the
    choices most decks never change: how the project appears (Own slides / Table row only / Leave
    out), its photograph, speaker notes, this deck's capability text, and Remove from deck (which
    asks first). For other slides, what the slide shows and, for the SDD profile, Role & Charter
    and FFC slides, Edit slide content.
  - **Deck**: the settings, below.
  - **Gaps**: per kind of gap, how many projects lack it; **Show** narrows the outline to them.
    The tab's badge counts the kinds open.
- **Add projects** (dialog): one list of every project narrowed by filters (search, ARPP year,
  technical and project category, status, completed between, proliferation), ticks kept across
  filters, Add with Undo, and Remove on projects already in the deck
  ([briefing-add-projects.md](briefing-add-projects.md)). Each saves in place.
- Adding, removing or reordering projects, block and settings saves and a capability text save
  rebuild the slides about a second later and keep the same project selected; a settings change
  shows a slide it affects. The Studio remembers the selected slide, the view and the inspector
  tab for the session.

### Deck settings (Deck tab)

Numbered steps, one open at a time, each with a one-line summary. Settings save themselves a moment
after the last change, one save at a time; leaving the page, posting another form or downloading
waits for a pending save. A save that collides with someone else's change shows one banner with
Reload.

1. **Slides in this deck**: the deck as blocks (`ProjectBriefingDeckBlocks`) in four groups,
   Opening, Overview, Projects and Closing. The cover and the project slides have switches; every
   other block (SDD profile, Role & Charter, stage-wise distribution, project-category and
   technical-category distributions, status table, FFC footprint) is listed only while it is in
   the deck, with Remove. **Add slide** opens a library of the rest; institution slides there have
   Edit, so their content can be set up before they are added. Overview blocks and the two opening
   institution slides move by their grip (drag, or Alt+Up / Alt+Down). A deck must keep the status
   table or the project slides. The order is stored as `overviewOrder` in `SelectionRulesJson`.
2. **Project slides**: one **slide style** for every project (Capability, Key points, Brief, Fact
   sheet, Key points only; "Capability and brief" only for decks that already use it), and "Show
   on each slide" chips: present stage, status remark, R&D cost, proliferation cost, and key
   points only where the style leaves them open (Brief, Capability and brief). The cost chips are
   the deck's cost choice, for the status table too. Brief has its layout (Automatic, Standard,
   Photo) while key points are off. Changing to or from Fact sheet reloads the page, which draws
   the list for the other layout.
3. **Sheet rows** (Fact sheet only): the rows on each sheet, ticked and ordered.
4. **Appearance**: theme, insignia, closing slide (Jai Hind or Thank You) and the optional
   classification marking printed in every slide's footer.

### Focus

**Focus** (header; from 576px) hides the PRISM top bar, the footer and the workspace rail in the
same tab, so the outline, slide and inspector take the whole window; the deck header stays and
back, links and autosave work as before. The label says what pressing does ("Focus" / "Exit
focus"). The choice is remembered per person in the browser (`prism.briefingStudio.focus` in
localStorage) and applied before first paint (`project-briefing-focus.js` in `<head>`). Escape does
not leave it (Escape belongs to dialogs and Present). Phones: no button, and a remembered focus
view is ignored below 576px.

### Widths

The panes follow the Studio's own width (`.pbd-studio-frame` is the `pbd-studio` size container),
not the window, so an open navigation narrows them. Three panes from 940px, the slide always the
widest: outline and inspector 20rem and 26rem from 2050px, up to 18rem and 23rem from 1400px,
13.5rem and 20rem from 1100px, 13rem and 19rem from 940px. From 700 to 939px the inspector moves
beneath the slide; below 700px one column. At 375px nothing scrolls sideways.

## Slides

- Fonts follow the PowerPoint (Aptos, then Segoe UI), so line breaks can differ slightly where
  Aptos is not installed.
- Project slides use one modest corner (rounded shapes capped at 0.12 in) and type sized for a
  room: capability points 14–18 pt by density (14 pt minimum), present status 12–14 pt, cost
  figures 15–17 pt, footer 9 pt. Long capability text and briefs continue onto "continued" slides
  rather than shrinking; a long Role & Charter continues on another slide rather than going below
  10 pt.
- A project shown as **Table row only** is a row in the status table (or in "Other projects in
  this briefing" when the deck has none) with no slides of its own; **Leave out** keeps it in the
  deck but not in the PowerPoint.

## History

Moved to [briefing-decks-history.md](briefing-decks-history.md) unchanged, oldest first:

- Review findings (1 Nov 2026)
- Plan: Briefing Studio
- Plan: Project key points
- Plan: Deck settings
- Plan: Studio usability
- Templates are slide designs (6 Nov 2026)
- Walkthrough fixes (6 Nov 2026)
- Independent review (6 Nov 2026)
- Library review (6 Nov 2026)
- Plan: Blocks, Project Officer decks and focus view (16 Nov 2026)
- Plan: The Studio is optional (19 Nov 2026)
