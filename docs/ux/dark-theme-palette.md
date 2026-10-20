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
- **Not yet covered:** other pages that paint literal light colours of their own. Known:
  the Dashboard body, the Remarks page (`project-remarks.css`), and anything with
  page-local `#fff`/`#f8fafc` backgrounds. `tasks.css` and `todo-widget.css` still follow
  `prefers-color-scheme` directly rather than the site choice. Convert these page by page
  (remap literals to the site tokens) before making Dark more prominent or the default.
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
- **Component overrides** live with their component: the shell in
  `navigation-shell.css`; shared Bootstrap and drawer rules at the end of `site.css`; the
  Overview's section tokens in `project-portfolio.css` ("V6"), which now read the site
  tokens instead of literals.
- **Converting a page:** replace literal light colours with the site tokens where the
  light value is identical (for example `#fff` → `var(--pm-card, #fff)`), so Light does not
  change; otherwise add a `[data-bs-theme="dark"]` rule next to the light one.

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
