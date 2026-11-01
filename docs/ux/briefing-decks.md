# Briefing decks (`/Workspace/BriefingDecks`)

Comdt and HoD users build shared PowerPoint briefing decks from live PRISM data. This document
records what the module does today, the problems found in the review of 1 Nov 2026, and the
agreed plan to turn the builder into a **Briefing Studio**.

## Where it lives

| Concern | Code |
| --- | --- |
| Page | `Pages/Workspace/BriefingDecks/Index.cshtml(.cs)`; editors `_InstitutionalProfileEditor`, `_RoleCharterEditor`, `_FfcGlobalFootprintEditor` |
| Deck persistence | `Services/ProjectBriefings/ProjectBriefingDeckService.cs`; options in `SelectionRulesJson` via `ProjectBriefingDeckConfigurationCodec` |
| Page data, readiness, slide estimate | `Services/ProjectBriefings/ProjectBriefingDataService.cs` |
| Slide generation | `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer*.cs`, `ProjectBriefingPowerPointExportService.cs` (`ComposeAsync` builds the deck; `GenerateAsync` also records the generation), `ProjectBriefingPresentationIntegrityValidator.cs` |
| Slide preview | `ProjectBriefingSlidePreviewRenderer.cs` (composed PowerPoint → positioned HTML); page handler `OnGetPreviewAsync`; `wwwroot/js/pages/project-briefing-preview.js` |
| Scripts and styles | `wwwroot/js/pages/project-briefing-decks.js`, `project-briefing-additional-slides.js`, `project-briefing-preview.js`; `wwwroot/css/pages/project-briefing-decks.css` |
| Access | `Policies.ProjectBriefingDecks.Manage` (Comdt, HoD); every deck is shared |
| Tests | `ProjectManagement.Tests/ProjectBriefings/*`, `wwwroot/js/projects/project-briefing-decks.test.js` |

## Slide preview

The builder shows the deck's slides under the settings bar. The server composes the same
PowerPoint that **Generate PowerPoint** downloads (without recording a generation) and reads it
back into positioned HTML: shapes, text, lines, pictures, tables and groups, in the composer's
own colours. So the preview and its slide count are exact, not estimated. Pictures are shrunk
to 960px and sent once each, however many slides use them. Every text value is encoded on the
server; the script inserts only that slide HTML and writes titles as text.

- Thumbnails fill the width: more columns on wider screens. Two rows show by default, with
  **Show all slides** for the rest.
- **Open viewer** (or any thumbnail) shows one slide as large as the window allows, with a strip
  of every slide: beside the slide from 1400px, below it on narrower screens. Arrow keys,
  Page Up/Down, Home and End move between slides; Escape closes.
- Adding, removing or reordering projects and saving a capability overview rebuild the
  preview about a second later. Settings saves reload the page, which rebuilds it too.
- Fonts follow the PowerPoint (Aptos, then Segoe UI), so line breaks can differ slightly where
  Aptos is not installed.

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

1. **Fixes** — done (#2836): bugs 1–3 and the empty-deck copy.
2. **Slide preview** — done: the preview is read back from the composed PowerPoint, so the
   slides and their count are exact. The pre-preview estimate in the checks panel is replaced
   by the exact count once the preview loads; removing the estimate code waits for the Studio.
3. **Status date on slides** — show the external-status date on the table, detail slides and
   update sheets, with the date counted in table pagination on both sides.
4. **Studio layout** — slide outline, live preview, inspector; every change saves in place with
   one conflict banner; ordering rules match the generator (fixes 4).
5. **Library and presets** — deck cards (purpose, owner, last generated and presented, search,
   pin, archive) and a three-step guide: purpose preset, projects (live rules or hand-picked),
   review and generate.
6. **Lifecycle** — Draft, Finalised (locked, PPTX stored on the LAN store), Presented; generation
   history; a "What changed since the last briefing" slide reusing the PO desk's What-moved facts.
7. **Per-project control and content** — per-briefing status and brief overrides, photo choice,
   speaker notes, continuation slides for long text (fixes 5), organisation content library.

### Wide screens

The Studio must use wide monitors, not just fit them:

- The page uses the `studio` shell (done with the preview): page and top bar take the full window, less the desk gutter, instead of the 1840px desk cap.
- **≥ 1600px**: three panes — outline (about 280px), preview (fluid, 16:9), inspector (about 380px).
- **≥ 2200px**: the preview shows the selected slide large with a filmstrip, or two slides side by
  side; the outline can show thumbnails instead of titles. Line length in the inspector stays
  capped so forms never stretch.
- **1200–1599px**: the inspector becomes a drawer over the preview.
- **< 1200px**: one column; outline and inspector open as sheets.
- The deck library grid adds columns as width grows (card minimum about 320px).
