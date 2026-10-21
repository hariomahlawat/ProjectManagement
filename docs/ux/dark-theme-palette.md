# Dark theme palette

PRISM's dark theme uses **neutral graphite**, not navy. It follows the approach of
mature work tools: near-black surfaces with the faintest cool tint, depth from lighter
surfaces rather than shadows, and blue used only as an accent.

Status: owner-approved direction (2026-10-21). The site-wide foundation is built and
**opt-in**: Light stays the default, so nobody sees dark unless they choose it.

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
- **Not yet covered:** waves 3 and 4 in [theme coverage](theme-coverage.md): the other
  project pages and reports, Action tracker, tasks (`tasks.css` still follows
  `prefers-color-scheme`), office reports and Admin. Dark stays opt-in until they are
  converted.
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
  `--officer-accent`, ...) are used both as fills behind white text and as text on the card.
  In dark they take a *balanced* tone: the same hue at relative luminance about 0.2, which
  keeps about 4:1 contrast both ways (`--pm-primary` is `#3f79e2`).
- **Component overrides** live with their component: the shell in
  `navigation-shell.css`; shared Bootstrap and drawer rules at the end of `site.css`; the
  Overview's section tokens in `project-portfolio.css` ("V6"), which now read the site
  tokens instead of literals.
- **Converting a page** (the wave workflow):
  1. `node tools/theme-dark-codemod.js wwwroot/css/<file>.css`. It swaps literals for
     tokens where the light value is identical (`#fff` → `var(--pm-card, #fff)`), then
     appends a generated `[data-bs-theme="dark"]` section that repaints every remaining
     literal by role: surfaces by lightness (page, card, tile, raised), slate and navy text
     to off-white or grey, hued text to the palette accent, pastel fills to tints of the
     card, focus rings kept in the accent, dense dark scrims kept dark, and solid status
     fills kept as they are. Review the section; it is a first pass.
  2. Open the page and every modal or offcanvas on it in the browser, in Dark and Light,
     and scan for light fills and text under 3.2:1, and look at screenshots: mid-grey
     islands slip past a luminance threshold. Set third-party widget variables by hand
     (FullCalendar). Watch for tokens used against their role: a text token as a
     background (the document reader used `--pm-text-contrast`), `--pm-card` as "white"
     text, and bare `<input>`s that relied on the browser's white default (dark falls back
     to the input colour at zero specificity; give them a page rule if they sit in a pill).
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
