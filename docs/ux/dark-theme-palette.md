# Dark theme palette

PRISM's dark theme uses **neutral graphite**, not navy. It follows the approach of
mature work tools: near-black surfaces with the faintest cool tint, depth from lighter
surfaces rather than shadows, and blue used only as an accent.

Status: owner-approved direction (2026-10-21). The site-wide foundation is built and
**opt-in**: Light stays the default, so nobody sees dark unless they choose it. A one-time card
(`wwwroot/js/core/whats-new.js`, markup in `Pages/Shared/_WhatsNew.cshtml`, after the Chat card) tells signed-in users the theme
exists and where to change it: Try Dark, Use Windows setting (System) or Not now. It is shown
per browser on up to three page loads until answered, never over an open dialog or panel or
while a form field has focus, and never to someone who has already used the switch.

- **Light text contrast.** Text in Light meets 4.5:1 on the page background (#f4f6fb), not
  just on white cards: links and outline buttons use `--pm-link` (#0b5ed7) and
  `--pm-muted-strong` (#5c636a) through Bootstrap's own variables, scoped to
  `:root:not([data-bs-theme="dark"])`; the shared greys (`--pm-text-muted`, `--pm-text-tertiary`,
  `--pm-accent-slate-mid` and friends) are 5:1 or better. Dark keeps its own values. For muted
  text use these tokens, not pale literals such as #98a2b3.
- **Switch.** User menu → Theme → Light / Dark / System. The choice is stored per browser
  in `localStorage` (`prism.theme`) and applied before first paint by
  `wwwroot/js/core/theme-preference.js`, loaded in `<head>` of `_Layout.cshtml` (no inline
  script). Light leaves `<html>` without `data-bs-theme`, exactly as before; Dark sets
  `data-bs-theme="dark"`; System follows the operating system live. Without storage access
  it falls back to Light. Printing always uses Light.
- **Covered and checked (2026-10-21):** layout shell (top bar, module sub-navigation,
  search suggestions, user menu, navigation drawer), shared Bootstrap components (body,
  cards, modals, offcanvas, dropdowns, forms, buttons, subtle/status utilities), and the
  Project Overview page with all 17 of its drawers and dialogs, on several projects
  (ongoing, completed, with and without remarks). Checked with a scan for light fills and
  for text under 3.2:1 contrast, in Dark and in Light.
- **Wave 1 covered and checked (2026-10-21):** Dashboard, My Workspace (every Project
  Officer and Command view, Conference Review and its detail page), Projects (repository
  list, Ongoing, Completed register, Create), Calendar (including FullCalendar's own
  variables) and the shared `site.css` components, with every modal and offcanvas on those
  pages. The to-do widget now follows the site choice instead of `prefers-color-scheme`.
- **Wave 2 covered and checked (2026-10-21):** Notebook (views, composer, open note;
  coloured notes become muted tints), Photos (library, viewer, filters, collections,
  People and the three review queues), Document Repository (list and card views, reader
  frame, upload), Global Search, and project Photos, Videos and Documents with their
  upload and reorder pages. PDF page rendering inside the reader was not seen (the dev
  storage has no PDF files); the reader stage stays dark as in Light.
- **Wave 3 covered and checked (2026-10-21):** Publications (overview, Brochure and
  Compendium builders; print proofs stay paper-white), Procurement Process, Briefing Decks,
  Ideation board and idea pages, IPR register, project Reports, the Completed-summary
  editor, Remarks, Analytics, Progress Review, Proliferation (records, overview, reports,
  manage) and the ARPP library. Older hand-written dark rules in a navy palette
  (IPR, officer conference, workspace, activity strip, ToT, FFC) were moved to graphite.
  Not seen: the Compendium cover editor (it needs a saved Compendium).
- **Wave 4 covered and checked (2026-10-21):** Action tracker (overview and board), Tasks,
  Decision Centre, ERP usage, Industry directory, Activities, office reports (Visits, Social
  media, Training, ToT, FFC proposals, detailed table, records, global footprint map, ARPP
  administration and reconciliation), Admin (every page in its menu), Notifications,
  Account settings, Developer and Sign-in. The FFC footprint map reads its country colours
  from `--ffc-map-*` custom properties (Light keeps its built-in values); Leaflet's own
  controls and tooltips are themed centrally. `tasks.css` no longer follows
  `prefers-color-scheme`. Not seen: the signed-out landing page (signed-in users are sent
  to the Dashboard) and action-task detail pages (no tasks in the dev data).
- **Coverage now:** 71 dark gaps site-wide, down from 8,567 before the theme work; all are
  deliberate (print proofs, the Procurement Process page's own always-dark journey, solid
  danger/warning button fills, keyframe glows). See [theme coverage](theme-coverage.md).

### Is Dark ready to offer?

Recommendation (the owner decides): **yes, offer it prominently, keep Light the default for
now.** Every page reachable from the navigation has been checked in Dark with its dialogs
and drawers, Light has been proven unchanged at each step, and the ratchet in `npm test`
stops new hard-coded colours. Before making **System** the default, run one release with
Dark opt-in so users can report anything the scans missed (rare states, data-dependent
colours such as charts with many series, exported previews), and review the few pages that
were not reachable in the dev data (above). Changing the default is one line in
`theme-preference.js` (`DEFAULT_PREFERENCE`).
- **Rollback.** Remove the `<script src="~/js/core/theme-preference.js">` line from
  `_Layout.cshtml`: every dark rule is scoped to `[data-bs-theme="dark"]`, so the site
  renders Light for everyone, including users who chose Dark.

## Principles

- **No pure black, no pure white.** `#000` behind white text makes text glow and smear
  (halation) and leaves no room for elevation. Main text is a soft off-white.
- **Elevation by lightness.** Each layer up is a step lighter: page → card → tile/input.
  Shadows are kept faint; borders are low-contrast hairlines.
- **Colour means something.** Blue marks the current or interactive thing (the current
  stage, links, focus, primary buttons). Green, amber and red are status only. Large areas
  are never tinted with an accent.
- **Status colours are lifted for dark.** Mid-tone green/amber/red lose contrast on dark
  surfaces; the dark variants below keep WCAG AA for text on the card surface.
- **Offline.** Fonts and icons are bundled (Mulish, Bootstrap Icons); nothing is fetched.

## Tokens

| Role | Light (current) | Dark | Notes |
| --- | --- | --- | --- |
| Page background | site `--pm-surface-bg` page tint | `#0e0f11` | Behind cards |
| Card, drawer, dialog | `--pm-card` (`#fff`) | `#16181b` | `--v6-surface` |
| Raised tile, input, hover | `#f8fafc` mix | `#1e2125` | `--v6-tile` |
| Hairline border | `--pm-border` mix | `#2c3035` | `--portfolio-border` |
| Strong border / focus outline base | — | `#3a3f45` | Dividers that must read |
| Main text | `#0f172a` | `#e6e8eb` | `--v6-text` |
| Muted text | `#64748b` | `#9aa0a6` | `--portfolio-muted`; labels and metadata |
| Accent | `#3b82f6` | `#6ea8fe` | `--v6-accent`; current stage, links, focus |
| Accent tint | accent 11% on card | accent 16% on card | `--v6-accent-soft`; icon tiles, active row |
| Good | `#16a34a` | `#4ade80` | `--v6-good` |
| Attention | `#f59e0b` | `#fbbf24` | `--v6-fair` |
| Alert | `#dc3545` | `#f87171` | `--v6-poor` |

## How it is applied

- **One token block.** `wwwroot/css/site.css` has a `:root[data-bs-theme="dark"]` block
  right after the light tokens. It remaps the site tokens (`--pm-card`, `--pm-surface-bg`,
  `--pm-surface-subtle`, `--pm-border`, `--pm-text`, `--pm-text-strong`, `--pm-text-muted`,
  status and accent tokens) and Bootstrap's (`--bs-body-bg`, `--bs-body-color`,
  `--bs-border-color`, `--bs-secondary-color`, `--bs-tertiary-bg`, `--bs-emphasis-color`,
  link, subtle and emphasis tokens). It is scoped to `:root` so it outranks the light
  `:root` block; that is what used to pin `--bs-body-bg` light under dark.
- **`--bs-body-bg` is the card surface in dark**, not the page: Bootstrap paints modals,
  offcanvas, dropdowns, list groups, active tabs and unchecked checkboxes with it. The page
  colour comes from `--pm-page-bg` through the `body` rule.
- **Raw graphite scale** for component overrides: `--pm-dark-page`, `--pm-dark-card`,
  `--pm-dark-tile`, `--pm-dark-raised`, `--pm-dark-hairline`, `--pm-dark-strong-border`,
  `--pm-dark-text`, `--pm-dark-text-muted`, `--pm-dark-accent` (defined only in dark).
- **Primary buttons** keep a solid `#2563eb` in dark so white text stays above AA; the
  lifted accent `#6ea8fe` is for text, marks and focus, not fills behind white text.
- **Tokens used both ways.** `--pm-primary` and page accent tokens (`--oc-success`,
  `--officer-accent`, ...) are used both as fills behind white text and as text on the card,
  and no single colour reaches 4.5:1 in both roles. In dark the token is the lifted text
  accent (`--pm-primary` is `#6ea8fe`, 7.5:1 on the card), and every rule that puts white
  text on it gets a generated fill of the same token mixed 55% with black
  (`color-mix(in srgb, var(--token) 55%, #000)`), which keeps white text at 4.5:1 or better.
- **Component overrides** live with their component: the shell in
  `navigation-shell.css`; shared Bootstrap and drawer rules at the end of `site.css`; the
  Overview's section tokens in `project-portfolio.css` ("V6"), which now read the site
  tokens instead of literals.
- **Converting a page** (the wave workflow):
  1. `node tools/theme-dark-codemod.js wwwroot/css/<file>.css`. It swaps literals for
     tokens where the light value is identical (`#fff` → `var(--pm-card, #fff)`), then
     appends a generated dark section (`:where([data-bs-theme="dark"]) <selector>`, so each
     dark rule has exactly the light rule's specificity and the cascade matches Light) that
     repaints every remaining literal by role: surfaces by lightness (page, card, tile, raised), slate and navy text
     to off-white or grey, hued text to the palette accent, pastel fills to tints of the
     card, focus rings kept in the accent, dense dark scrims kept dark, and solid status
     fills kept as they are. Kept colours and state rules (`.x.active`, `.x:hover`) are
     stated again in the dark section so a repainted rule for another class cannot override
     them. Review the section; it is a first pass. Regenerate by deleting the generated
     section and re-running; hand-written dark rules go above it and survive.
     `--regraphite` moves older hand-written dark rules from navy to the graphite steps
     (accents, status tints and white are kept; light islands with `color-scheme: light` are
     skipped).
  2. Open the page and every modal or offcanvas on it in the browser, in Dark and Light,
     and scan for light fills and text under 3.2:1, and look at screenshots: mid-grey
     islands slip past a luminance threshold. Set third-party widget variables by hand
     (FullCalendar). Watch for tokens used against their role: a text token as a
     background (the document reader used `--pm-text-contrast`), `--pm-card` as "white"
     text, and bare `<input>`s that relied on the browser's white default (dark falls back
     to the input colour at zero specificity; give them a page rule if they sit in a pill).
     Bootstrap's fixed greys (`--bs-gray-*`), `.text-dark` and `.table-light` are remapped
     centrally in `site.css`. Print proofs and previews of printed output are light
     islands: re-apply Light's tokens on the proof container with `color-scheme: light`.
  3. `node tools/theme-coverage.js --write` to lower the baseline. `npm test` fails if any
     stylesheet gains literals or gaps (a gap is a literal no dark rule repaints; it
     includes colours kept on purpose, such as white text on a solid fill).
  4. Prove Light is unchanged: every non-dark rule must be identical after resolving
     token fallbacks.

### Hard-coded light surfaces on the Overview (resolved)

The 2026-10-21 scan found these painting light backgrounds of their own. All now follow
the theme, most through the central token remap and the rest by explicit rules:

- `#stageRequestModal .modal-header` / `.modal-footer` (`background: #fff` → `--pm-card`)
- `.lifecycle-precision-option label`, `.lifecycle-action-switch`, `.lifecycle-completion-field`, `.lifecycle-actions-modal__footer` (token remap)
- `.proliferation-choice__body` (including its checked states), `.proliferation-editor__audit` (token remap)
- `.plan-editor-guidance`, `.plan-editor-history`, `.actuals-editor__footer` (token remap)
- `.stage-update-summary`, `.project-jdp-picker__results` (token remap; `#fff` → `--pm-card`)
- Bootstrap `.nav-tabs .nav-link.active` and unchecked `.form-check-input` (follow the dark `--bs-body-bg`)
- Also fixed: Overview card headers (`.pm-card-header` `#fff`), `.project-document-preview`, `.pm-pill-muted`, the record-health summary label, and solid primary buttons.
