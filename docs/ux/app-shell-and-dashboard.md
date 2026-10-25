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

- The IST date (uppercase eyebrow), a greeting by IST time of day ("Good morning" before 12:00,
  "Good afternoon" before 17:00, "Good evening" after) with the user's rank and full name (falls
  back to the user name), and one line on what the page covers.
- A short spectrum rule under the greeting uses the Dashboard's own accent tokens, echoing the prism
  on the sign-in page.
- The load time and **Open analytics** stay on the right.

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
