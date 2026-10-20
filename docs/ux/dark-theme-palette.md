# Dark theme palette

PRISM's planned dark theme uses **neutral graphite**, not navy. It follows the approach of
mature work tools: near-black surfaces with the faintest cool tint, depth from lighter
surfaces rather than shadows, and blue used only as an accent.

Status: owner-approved direction (2026-10-21). The site has no dark theme switch yet; the
Project Overview page (`wwwroot/css/pages/project-portfolio.css`, "V6" section) already
reads these values from section tokens, so it flips when the site theme lands.

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

## Applying it site-wide

When the site theme is built, define these as site tokens under `[data-bs-theme="dark"]`
in `wwwroot/css/site.css` (including Bootstrap's `--bs-body-bg`, `--bs-body-color`,
`--bs-border-color`, `--bs-secondary-color` and `--bs-tertiary-bg`), then point the
Overview section tokens at them instead of the literal values.

Remap the site's own tokens (`--pm-card`, `--pm-surface-bg`, `--pm-surface-subtle`,
`--pm-border`, `--pm-text-strong`, `--pm-text-muted`) centrally rather than overriding
components one by one: most components read those tokens, so they follow automatically.
Today `--bs-body-bg` is pinned light even under `data-bs-theme="dark"`.

### Known hard-coded light surfaces

A scan of every Project Overview drawer and dialog with `data-bs-theme="dark"` set
(2026-10-21) found these painting light backgrounds of their own; check each once the
tokens are remapped:

- `#stageRequestModal .modal-header` / `.modal-footer` (`background: #fff`)
- `.lifecycle-precision-option label`, `.lifecycle-action-switch`, `.lifecycle-completion-field`, `.lifecycle-actions-modal__footer`
- `.proliferation-choice__body` (including its checked states), `.proliferation-editor__audit`
- `.plan-editor-guidance`, `.plan-editor-history`, `.actuals-editor__footer`
- `.stage-update-summary`, `.project-jdp-picker__results`
- Bootstrap `.nav-tabs .nav-link.active` and unchecked `.form-check-input` (they follow `--bs-body-bg`)
