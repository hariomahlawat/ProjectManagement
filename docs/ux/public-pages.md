# Public pages: landing, sign-in and status pages

What signed-out visitors see: the landing page (`/`), the sign-in page
(`/Identity/Account/Login`) and the status pages: sign out (`/Identity/Account/Logout`),
access denied (`/Identity/Account/AccessDenied`) and error (`/Error`). Signed-in users never see the landing page; `IndexModel` sends
them to their default landing page.

Redesigned 2026-10-24. The old pages described an early PRISM (an 8-step lifecycle, no
desks, chat or media) and used a raster hero image.

## Concept

**One beam in, every view out.** The PRISM mark (`Pages/Shared/_PrismSpectrum.cshtml`) is an
inline SVG: one beam, the project record, enters a glass prism and leaves as six coloured
rays, one per area of the platform (Teamwork, Office records, Media, Documents, Projects,
Desks). The same six hues colour the workflow track and the feature tiles, so the page reads
as one system.

- The landing hero draws the mark with labels (`model: true`); the sign-in page draws the
  bare artwork (`model: false`), whose rays run on past the SVG box and behind the glass
  card. Gradient ids are fixed, so render it at most once per page.
- On phones the SVG labels would be too small, so they are hidden and the six areas show as
  chips under the artwork.
- The beam pulse, ray shimmer, travelling light on the workflow track and mock caret are
  switched off under `prefers-reduced-motion`.

## Status pages

Sign out, Access denied and Error share one layout: a centred glass card over the prism
scene. A white beam enters from the left, the card sits where it splits, and a faint
six-colour fan leaves to the right behind it. Each card has a hue (`lp-hue-*` on the root),
an icon, an eyebrow, a heading, one sentence and the next action.

| Page | Hue | Signed out | Signed in |
| --- | --- | --- | --- |
| Sign out | 3 (green) | "You have been signed out": Sign in again, Back to home | Signing out is a POST, so a GET while signed in asks "Sign out of PRISM?" with a Sign out button (posts to the same handler, antiforgery token included) and Stay signed in |
| Access denied | 2 (amber) | Sign in (returns to `ReturnUrl` when it is local), Back to home | Go to dashboard |
| Error | 1 (rose) | Back to home, Sign in | Go to dashboard |

- Access denied keeps its two messages word for word (tests assert the FFC one) and shows the
  requested page, when it is a local URL, so the user can quote it to the administrator.
- Error shows the request id as "Reference" (selectable in one click) when `ShowRequestId`
  is true. It no longer shows the template's Development-mode text to end users.
- These pages also render signed in (Access denied and Error mostly are). `.status--in-app`
  then drops the slide-under-the-capsule offset and sits below the app top bar.

## Where the copy comes from

Keep the copy true to the current application; update it when a module is added or retired.

| Section | Source of truth |
| --- | --- |
| Workflow track (`#workflow`) | Rendered from `ProcurementWorkflow.StageDefinitionsFor(PlanConstants.DefaultStageTemplateVersion)` via `IndexModel.Stages`, so stage codes, names and the count follow the code. |
| Inside PRISM tiles (`#inside`) | Hand-written from the current modules: Project Overview, [Command desk](command-desk.md), [Briefing desk](project-officer-briefing-desk.md), Document Repository and [search](search.md), office records, chat and tasks, Media Library, publications and analytics. The mini mocks are decorative (`aria-hidden`) and show no real data. |
| Assurance (`#trust`) | Deployment and security invariants in `AGENTS.md` and `docs/engineering-invariants.md` (offline LAN, server-side access checks, history). |

## Files

| Concern | File |
| --- | --- |
| Landing markup | `Pages/Index.cshtml` |
| Sign-in markup | `Areas/Identity/Pages/Account/Login.cshtml` |
| The prism mark | `Pages/Shared/_PrismSpectrum.cshtml` |
| Signed-out top bar and footer | `Pages/Shared/_Layout.cshtml`, `wwwroot/css/public-shell.css` (loaded for every signed-out page; site tokens only) |
| Landing styles, shared tokens, buttons, prism styles | `wwwroot/css/landing.css` |
| Sign-in styles | `wwwroot/css/auth-login.css` (landing tokens only; no colour literals) |
| Status pages markup | `Areas/Identity/Pages/Account/Logout.cshtml`, `Areas/Identity/Pages/Account/AccessDenied.cshtml`, `Pages/Error.cshtml` |
| Status page styles | `wwwroot/css/status-page.css` (loaded after `landing.css`; landing tokens only; no colour literals) |
| Caps Lock hint and busy submit button | `wwwroot/js/pages/auth-login.js` (the form works without it) |
| Pointer-following glass highlight | `wwwroot/js/pages/glass-light.js` (decorative; skipped for touch and Reduce Motion) |

## Theme

- All colours are `--lp-*` tokens in two blocks at the top of `landing.css`: `:root` (Light)
  and `:root[data-bs-theme="dark"]`. Surfaces and text use the site tokens (`--pm-card`,
  `--pm-text-strong`, ...), so they follow the site's graphite palette in Dark.
- The landing call-to-action panel stays dark (night) in both themes and uses the lifted hues
  (`--lp-lift-*`). Everything else, including the sign-in scene, follows the theme.
- Signed-out visitors get a Light / Dark / System switch in the top bar. It uses the same
  `data-theme-preference-value` controls and `prism.theme` storage as the signed-in user menu
  (`wwwroot/js/core/theme-preference.js`), so a choice made before sign-in carries over.
- Mulish ships 400, 600 and 700 only; the pages never ask for a heavier weight, which the
  browser would fake.

## Glass

The surfaces follow a Liquid Glass style (translucent, light-catching layers floating over
the content). One recipe, defined on `.lp` in `landing.css` ("GLASS" section):

| Token | Role |
| --- | --- |
| `--lp-glass`, `--lp-glass-strong` | Translucent frost: white at 56% / 74% in Light, 5% / 8% in Dark (smoked glass). |
| `--lp-glass-filter` | `blur(22px) saturate(1.7)` behind the surface. |
| `--lp-glass-rim` | Hairline edge: dark at 9% in Light, white at 12% in Dark. |
| `--lp-glass-shadow` | Top sheen, inner edge and a soft drop shadow. |

- Used by the floating header capsule, the badge, ghost buttons, the theme switch, the
  feature tiles, the assurance cards, the sign-in card and the status-page cards. Long text and form fields sit on
  denser panes (`--lp-glass-strong`); focused inputs turn solid.
- `.lp-aurora` layers (slowly drifting spectrum light, faded at section edges) give the glass
  something to blur. The page slides under the header capsule (`--lp-header-h`).
- **Reduce Transparency** (`prefers-reduced-transparency`) makes every surface solid and hides
  the aurora; **Reduce Motion** stops the drift and the pointer highlight. Browsers without
  `backdrop-filter` get the denser frost.
- Cost: blur runs on the GPU. Only a handful of layers blur at once; do not add glass to
  long lists or tables inside the application.
