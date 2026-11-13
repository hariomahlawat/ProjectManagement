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
rays, one per area of the platform, in refraction order and leading with the record itself
(Projects, Desks, Teamwork, Office records, Media, Documents). The same six hues colour the
workflow track and the feature tiles, so the page reads as one system.

The glass body is drawn to read as crystal in both themes: a light wedge where the beam
spreads inside, six faint colour wedges from the entry point to each exit segment (the
spectrum already separating inside the glass), a shaded exit face for thickness, a soft
ground shadow, and a bright core down the beam and each ray so they read as light rather
than rods or ribbons. All of it is tokens in `landing.css` (`--lp-fan-alpha`,
`--lp-split-alpha`, `--lp-glass-face-alpha`, `--lp-ground-alpha`, `--lp-beam-core-alpha`,
`--lp-ray-core-alpha`).

- The landing hero draws the mark with labels (`model: true`); the sign-in page draws the
  bare artwork (`model: false`), whose rays run on past the SVG box and behind the glass
  card. Gradient ids are fixed, so render it at most once per page.
- On phones the SVG labels would be too small, so they are hidden and the six areas show as
  chips under the artwork.
- The beam pulse, ray shimmer, travelling light on the workflow track and mock caret are
  switched off under `prefers-reduced-motion`.

## Sign-in: light writes the name

Added 2026-11-13. On arrival the sign-in page plays one opening sequence, about two
seconds: the beam enters the prism, the six rays leave, a point of light traces PRISM ERP
letter by letter as a fine spectrum outline, the outline takes a faint tint and gives way
to the solid wordmark, then the tagline and strap line rise in. It is kept quiet on
purpose: one outline pass, no flashing fill, and a glow on the dark scene only (on a pale
ground a glow just blurs the letters).

- **When it plays.** Only on a GET (`auth--intro` on the scene; a failed sign-in comes back
  as a POST and shows the page at rest), only on the wide layout (above 960px) and only when
  Reduce Motion is off. The form is ready to type in from the first frame; nothing waits for
  the sequence.
- **How the letters are traced.** `auth-login.js` lays one outlined SVG letter exactly over
  each letter of the real wordmark text (positions measured from the text itself once Mulish
  has loaded) and `auth-login.css` animates them. The solid name is plain HTML text. If the
  script does not run, or the page took longer than 1.3 s to get there, the name simply fades
  in by CSS. The script starts the name's wait by renaming its animation
  (`auth-name` to `auth-name-traced`), which keeps the two in step without inline styles.
- **Sending.** The rays brighten and one pulse runs down the beam when the form is sent
  (`auth--sending`). Nothing reacts to typing.
- **Greeting.** The card heading is "Good morning / afternoon / evening" by the computer's
  clock ("Welcome back" without the script), with "Sign in to PRISM." under it.
- **One screen.** The artwork shrinks on short screens so the wordmark and tagline stay on
  the first screen (they were cut off at 1080p with 125% display scaling). The bare
  artwork's rays therefore run on to x = 1800, so they still pass behind the card when the
  prism is drawn small.

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

## About (signed in)

`/Developer`, linked as "About" from the signed-in footer, is the maker's page first and
PRISM's second. It tells the prism story inside the app:

- **Hero: the developer.** The SDD archer stands in warm light; the light of its arrow
  leaves the arrowhead and opens into the six-colour spectrum behind "Designed and built by",
  the developer's name and division. Email and phone are copy-to-clipboard buttons.
- **The work: PRISM ERP.** The PRISM icon (a night tile that stays dark in both themes and
  draws the bare mark, `model: false`, with its night values), the tagline and the six
  areas from `Utilities/PrismAreas.cs`. That class is also the list the prism mark draws its
  rays from, so the two cannot drift apart.
- **This installation**: version (`App:Version` when set, otherwise the assembly version),
  build commit, build time, process start, runtime, environment and the offline-LAN note.
  `Utilities/AppBuildInfo.cs` reads these once from the assembly the SDK stamped
  (`1.0.0+<sha>` from Source Link). Times are IST. "Copy details" copies them as plain text
  for a fault report.

Motion: the hero plays once on load (the archer lands, the spectrum opens from the arrow,
the lines rise in and one sweep of light crosses the name); lower sections rise in as they
scroll into view (`wwwroot/js/pages/about-prism.js`). Reduce Motion shows everything at
rest. Copying falls back to `execCommand('copy')` because PRISM is often served over plain
http on the LAN, where `navigator.clipboard` is unavailable. Styles:
`wwwroot/css/pages/developer.css` (landing tokens only, no colour literals).

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
| Greeting, traced wordmark, Caps Lock hint and busy submit button | `wwwroot/js/pages/auth-login.js` (the form works without it) |
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
the content). One recipe, defined on `body:has(.lp)` and `.lp` in `landing.css` ("GLASS"
section), so the floating header capsule outside `.lp` shares it:

| Token | Role |
| --- | --- |
| `--lp-glass`, `--lp-glass-strong` | Translucent frost: white at 46% / 70% in Light, 6% / 9% in Dark (smoked glass). |
| `--lp-glass-surface` | The frost with a soft diagonal gleam across its upper-left (`--lp-glass-gleam`). |
| `--lp-glass-filter` | `blur(18px) saturate(1.7)` in Light, `blur(20px)` in Dark, behind the surface. |
| `--lp-glass-rim` | Hairline edge: dark at 9% in Light, white at 12% in Dark. |
| `--lp-glass-rim-ring` | The directional rim, drawn by each surface's `::before`: bright at the top-left (`--lp-glass-rim-hi`), shaded at the bottom-right (`--lp-glass-rim-lo`), masked to 1px. |
| `--lp-glass-shadow` | Top sheen, inner edge, the lens (a brighter band just inside the edge, `--lp-glass-lens`) and a soft drop shadow. |

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
