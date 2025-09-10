# Razor Pages

This module summarises the UI components exposed to end users.

## Login
* `Areas/Identity/Pages/Account/Login.cshtml` – form that collects a username and password; uses built-in tag helpers for validation.
* `Areas/Identity/Pages/Account/Login.cshtml.cs` – authenticates the user with `SignInManager`, handles disabled accounts and lockouts, and records events via `ILogger`.

## Logout
* `Areas/Identity/Pages/Account/Logout.cshtml` – simple confirmation page after logout.
* `Areas/Identity/Pages/Account/Logout.cshtml.cs` – signs the user out and clears the session.

## Account management
* `Areas/Identity/Pages/Account/Manage/Index.cshtml` – entry point for signed-in users to manage their account, including password changes.

## Change Password
* `Areas/Identity/Pages/Account/Manage/ChangePassword.cshtml` – form for updating the current user's password.
* `Areas/Identity/Pages/Account/Manage/ChangePassword.cshtml.cs` – validates the old password, updates it, clears the `MustChangePassword` flag and refreshes the sign-in cookie.

## Dashboard
* `Pages/Dashboard/Index.cshtml` – landing page after sign-in. It shows an attention band when tasks are overdue or due today and renders the **My Tasks** widget on the right column alongside placeholder content for future widgets.
* `Pages/Shared/_TodoWidget.cshtml` – partial responsible for listing, adding, pinning, completing, snoozing, priority changes and deleting tasks. It uses a compact single-line layout with truncated titles, priority dots, due-date chips and a quiet overflow menu triggered by a circular icon-only button. Badge counters on the header show **Overdue** (due time before now) and **Due today** (remaining tasks before midnight IST) counts using exclusive buckets so items never appear in both. Bootstrap's caret is suppressed and the dropdown is initialised by `wwwroot/js/todo.js` to render in the document body with fixed positioning and auto-flip logic to avoid clipping near the right edge. Menus carry a `.todo-menu` class so they retain a high z-index even after being moved to `<body>`, and the script ensures only one dropdown stays open at a time. It also wires up a shared confirmation modal for completion and deletion actions and is loaded conditionally via `wwwroot/js/init.js`. The widget is rendered only on the dashboard's right column and opens to the left by default using `dropstart`.
* `Pages/Dashboard/_CelebrationsWidget.cshtml` – partial that lists upcoming birthdays and anniversaries split into **Today**, **Next 7 days** and **This month** sections. Items show a coloured type dot (pink for birthdays, teal for anniversaries), the name and a tiny relative-time badge. A plus icon lets users add a "Wish &lt;Name&gt;" task due on the celebration date. The widget fetches data from `/celebrations/upcoming` and is loaded on demand by `wwwroot/js/celebrations.js`.

## Tasks
* `Pages/Tasks/Index.cshtml` – full management interface with filter tabs, search and page size selection. Tasks are shown in a grouped list (Overdue / Today / Upcoming / Completed) with priority dots, due-date chips and calm overflow menus triggered by icon-only circular buttons. Grouping and chip labels use India Standard Time, marking an item **Overdue** when its due moment has passed and **Today** only when it is still in the future but due before midnight IST. Rows support inline edits, drag-and-drop reordering and collapsible note fields. `wwwroot/js/tasks-page.js` wires up row action visibility, drag reordering and done checkbox auto-submit while respecting anti-forgery tokens and CSP rules (no inline scripts). The same `todo.js` script handles dropdown placement, ensures only one menu is open at a time and preserves z-index with the `.todo-menu` class, and it is brought in dynamically through `init.js` only when the list is present. Completing a task surfaces a small success banner with an Undo action, and the Completed tab offers a **Clear all completed** button with a confirmation prompt. The quick-add box accepts tokens like `tomorrow`, `today`, `mon`, `next mon`, `!high` and `!low` to set due dates and priority while stripping them from the final title.

## Admin analytics
* `Areas/Admin/Pages/Analytics/Logins.cshtml` – scatter chart rendered with Chart.js (loaded as a global script) showing login times over a selectable window with an office-hours band, percentile lines (median and P90 only when data exists), weekend highlighting and per-user filter. Dataset decimation keeps rendering fast on large ranges. Odd logins are listed below the chart, can be exported to CSV and points link to their audit log entries.
