# Top bar, Dashboard and workspace headers

What a signed-in user sees first: the top bar on every page, and the Dashboard header they land on
after signing in. The aim is that the step from the [sign-in page](public-pages.md) to the app does
not feel like a drop in quality.

## Where it lives

| Concern | Code |
| --- | --- |
| Top bar markup | `Pages/Shared/_Layout.cshtml` (`pm-topbar`), `Pages/Shared/_LoginPartial.cshtml` (user menu) |
| Top bar styles | `wwwroot/css/navigation-shell.css` |
| Dashboard header | `Pages/Dashboard/Index.cshtml` (`db-header`), `IndexModel.Greeting` / `GreetingFor` |
| Dashboard styles | `wwwroot/css/pages/dashboard.css` |

## Top bar

Polished 2026-10-25 so the step from the sign-in page into the app is not a drop in quality:

- **One header, two tiers.** The top bar (52px) and the module sub-nav (46px) share one surface,
  a light frost of the card colour (`color-mix` at 94%, `blur(14px) saturate(1.4)`), with a single
  hairline between them and a soft shadow only where the sub-nav meets the scrolled page. The
  heights are fixed because other pages offset their sticky elements by them. Reduce Transparency
  and browsers without `backdrop-filter` get the solid card colour.
- **The prism signature.** A 2px rule along the top edge of the top bar runs the six spectrum hues
  (`--pm-spec-1..6`) in refraction order. It is the only spectrum in the shell; the Dashboard title
  keeps its own rule.
- **One active language.** Top tabs are 13px (`--pm-text-secondary`, active `--pm-text-strong`)
  with a short primary underline that grows in; nothing is filled. Sub-nav items are 12.5px pills,
  the active one a soft primary tint (`--pm-primary-soft` / `--pm-primary-strong`) with no border
  or bar. The module action (Create project) is the one filled button, in `--pm-primary`.
- **Brand lockup.** "PRISM ERP" uses the public pages' two-weight wordmark (`PRISM` strong,
  `ERP` muted) with the crest at 28px; the sub-nav's hidden alignment spacer repeats it.
- `wwwroot/css/navigation-shell.css` uses site tokens only for the shell, so Dark needs no remap
  beyond the filled button (dark text on the lifted blue); `node tools/theme-coverage.js` keeps it so.
- The module tabs (Dashboard, My Workspace, Calendar, Notebook, Photos, Projects, FFC, Documents)
  must fit without clipping on desktop widths. Room is made in this order:
  - **1600px and wider:** search box, avatar and the signed-in name (at most 10rem, then "…").
  - **Below 1600px:** the name gives way to the avatar. The user menu opens with "Signed in as
    *name*".
  - **Below 1400px:** the search box narrows and drops its Ctrl K hint.
  - **Below 1280px:** the search box becomes its icon (opens the search page).
  - **768–1199px (tablets):** if the tabs still do not fit, they scroll sideways and the right edge
    fades, so the last tab reads as "more this way". Every tab is also in **MENU**.
  - **Below 768px:** the tabs are hidden; **MENU** carries them.
- Checked at 1200, 1280, 1366, 1440 and 1600px with a long user name: no tab is clipped.

## Dashboard header

- One line: the page title ("Dashboard"), the IST date beside it, and the one action (**Open
  analytics**) on the right. There is no greeting, eyebrow, subtitle or "Loaded …" stamp; the load
  time is the title's tooltip. `IndexModel.Greeting` / `GreetingFor` still exist for other callers
  and tests but the page no longer shows them.
- A 3px spectrum rule under the title (`--pm-spec-1..6`, the sign-in prism's six hues) is the only
  place the whole rainbow appears in the page content (the top bar's edge rule is the shell's).

## Dashboard colour rules (2026-10-25)

Ink and white with a few small jewels, so the page does not read as a downgrade after the public
pages, and never as a barber pole:

- The page is neutral (`--pm-main-bg`); cards are white (graphite in Dark) with a visible hairline
  edge and a soft shadow (`--pm-shadow-card`). Card titles and headline figures use
  `--pm-text-strong`; captions use `--pm-text-muted` (5:1 or better) at 12px or above.
- Each card owns **one** hue from the spectrum tokens (`db-hue-4` cyan, `db-hue-5` blue, `db-hue-6`
  violet; `--db-hue` inside the card). It appears only on the card's icon and its sparkline or lead
  bar (meters, the lifecycle bar, a 3px rule on the primary tile). No tinted fills behind text, and
  never two hues inside one card. Sparklines read `--db-hue` and `--db-surface` from the stylesheet
  (`wwwroot/js/pages/dashboard.js`), so Dark gets the lifted hues automatically.
- Green, amber and red mean good, waiting and failed (the change arrows, the document-processing
  card, the map's planned units) and are not used as decoration.
- `wwwroot/css/pages/dashboard.css` uses site tokens only; `node tools/theme-coverage.js` keeps it so.

## Type sizes

- Dashboard text is at least 12px (`.75rem`); small uppercase labels at least 11px (`.6875rem`);
  what used to be 11–12.5px is now 13px (`.8125rem`). The same floor applies to the FFC map widget
  (`wwwroot/css/widgets/ffc-simulator-map.css`). Keep new Dashboard text at or above these floors.
- My Workspace uses the same floors: the workspace rail, Officer workload, Project portfolio and
  the other command views (`wwwroot/css/command-workspace.css`) and Conference review
  (`wwwroot/css/officer-conference.css`).

## My Workspace page headers

Every command view opens with the same header: a small "Command oversight" / "Project monitoring"
label, a large title and one line on what the view shows (`cw-page-header` for Officer workload and
Project portfolio, matching Conference review and Briefing decks; the Command desk has its own).
