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
| Slide generation | `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer*.cs`, `ProjectBriefingPowerPointExportService.cs`, `ProjectBriefingPresentationIntegrityValidator.cs` |
| Scripts and styles | `wwwroot/js/pages/project-briefing-decks.js`, `project-briefing-additional-slides.js`; `wwwroot/css/pages/project-briefing-decks.css` |
| Access | `Policies.ProjectBriefingDecks.Manage` (Comdt, HoD); every deck is shared |
| Tests | `ProjectManagement.Tests/ProjectBriefings/*`, `wwwroot/js/projects/project-briefing-decks.test.js` |

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

1. **Fixes** — bugs 1–3, the empty-deck copy, and the status date on slides.
2. **One slide plan and an SVG preview** — the composer draws through one canvas; an SVG back end
   gives exact thumbnails offline, and the page count comes from the real plan (fixes 6).
3. **Studio layout** — slide outline, live preview, inspector; every change saves in place with
   one conflict banner; ordering rules match the generator (fixes 4).
4. **Library and presets** — deck cards (purpose, owner, last generated and presented, search,
   pin, archive) and a three-step guide: purpose preset, projects (live rules or hand-picked),
   review and generate.
5. **Lifecycle** — Draft, Finalised (locked, PPTX stored on the LAN store), Presented; generation
   history; a "What changed since the last briefing" slide reusing the PO desk's What-moved facts.
6. **Per-project control and content** — per-briefing status and brief overrides, photo choice,
   speaker notes, continuation slides for long text (fixes 5), organisation content library.

### Wide screens

The Studio must use wide monitors, not just fit them:

- The Studio opts out of the 1840px desk shell and uses the full window, like other builders.
- **≥ 1600px**: three panes — outline (about 280px), preview (fluid, 16:9), inspector (about 380px).
- **≥ 2200px**: the preview shows the selected slide large with a filmstrip, or two slides side by
  side; the outline can show thumbnails instead of titles. Line length in the inspector stays
  capped so forms never stretch.
- **1200–1599px**: the inspector becomes a drawer over the preview.
- **< 1200px**: one column; outline and inspector open as sheets.
- The deck library grid adds columns as width grows (card minimum about 320px).
