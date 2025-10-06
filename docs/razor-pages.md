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
* `Pages/Dashboard/Index.cshtml` – landing page after sign-in. It shows an attention band when tasks are overdue or due today and renders the **My Tasks** and **Upcoming events** widgets on the right column alongside placeholder content for future widgets.
* `Pages/Shared/_TodoWidget.cshtml` – partial responsible for listing, adding, pinning, completing, snoozing, priority changes and deleting tasks. It uses a compact single-line layout with truncated titles, priority dots, due-date chips and a quiet overflow menu triggered by a circular icon-only button. Badge counters on the header show **Overdue** (due time before now) and **Due today** (remaining tasks before midnight IST) counts using exclusive buckets so items never appear in both. Bootstrap's caret is suppressed and the dropdown is initialised by `wwwroot/js/todo.js` to render in the document body with fixed positioning and auto-flip logic to avoid clipping near the right edge. Menus carry a `.todo-menu` class so they retain a high z-index even after being moved to `<body>`, and the script ensures only one dropdown stays open at a time. It also wires up a shared confirmation modal for completion and deletion actions and is loaded conditionally via `wwwroot/js/init.js`. The widget is rendered only on the dashboard's right column and opens to the left by default using `dropstart`.
* `Pages/Dashboard/_UpcomingEventsWidget.cshtml` – lists the next five calendar entries within the coming month and links to the full calendar.

## Tasks
* `Pages/Tasks/Index.cshtml` – full management interface with filter tabs, search and page size selection. Tasks are shown in a grouped list (Overdue / Today / Upcoming / Completed) with priority dots, due-date chips and calm overflow menus triggered by icon-only circular buttons. Grouping and chip labels use Indian Standard Time via `TimeZoneHelper`, marking an item **Overdue** when its due moment has passed and **Today** only when it is still in the future but due before midnight IST. Rows support inline edits, drag-and-drop reordering and collapsible note fields. `wwwroot/js/tasks-page.js` wires up row action visibility, drag reordering and done checkbox auto-submit while respecting anti-forgery tokens and CSP rules (no inline scripts). The same `todo.js` script handles dropdown placement, ensures only one menu is open at a time and preserves z-index with the `.todo-menu` class, and it is brought in dynamically through `init.js` only when the list is present. Completing a task surfaces a small success banner with an Undo action, and the Completed tab offers a **Clear all completed** button with a confirmation prompt. The quick-add box accepts tokens like `tomorrow`, `today`, `mon`, `next mon`, `!high` and `!low` to set due dates and priority while stripping them from the final title.

## Admin analytics
* `Areas/Admin/Pages/Analytics/Logins.cshtml` – scatter chart rendered with Chart.js (loaded as a global script) showing login times over a selectable window with an office-hours band, percentile lines (median and P90 only when data exists), weekend highlighting and per-user filter. Dataset decimation keeps rendering fast on large ranges. Tooltips and the odd-logins table show friendly user names (falling back to email or "(deleted)"), CSV export includes both name and ID, and points link to their audit log entries.

## Calendar
* `Pages/Calendar/Index.cshtml` – FullCalendar-based interface offering month, week and list views. Events load from `/calendar/events` and show details in an offcanvas panel with Markdown rendering and a quick “Add to My Tasks” action. Users in the Admin, TA or HOD roles can create, drag, resize, edit and delete events through a separate offcanvas form.
  * Compact category pills sit in the top bar between view switches and navigation. Each pill shows a coloured dot and live event count, and a legend below the calendar repeats these counts.
  * Clicking a pill filters existing DOM nodes client-side; “All” restores every event without refetching.
  * Prev/next buttons and a dynamic title show the current range.
  * View buttons retain an active state and small screens automatically switch to a list view.
  * Business hours highlight 08:00–18:00 Monday–Saturday and Sundays get a light tint; an empty message appears when no events are visible.
  * Categories include Visit, Insp, Conference and Other; legacy strings such as "Training" or "TownHall" are mapped to the nearest canonical value and unknown strings default to Other.
  * Local times are respected when editing; all-day events use date-only inputs. Clicking an event pre-fills the form and shows a Delete option, while **New Event** clears any stale data.
  * Saving or moving an event surfaces a toast with an Undo action, and non-editors see a read-only offcanvas with Markdown details and an **Add to My Tasks** button.
  * Print styles hide chrome so month grids can be printed cleanly.
  * FullCalendar's global bundle injects its own styles at runtime, so no separate CSS links are required.
  * `calendar.js` detects which FullCalendar plugins are present and only registers those, allowing either the single bundle or individual plugin globals. Load Bootstrap's bundle, then FullCalendar, then the page script.

* `Areas/Admin/Pages/Calendar/Deleted.cshtml` – admin-only table listing soft-deleted events with a Restore action.

## Notifications centre
* `Pages/Notifications/Index.cshtml` – lists the most recent 50 notifications (lazy-loaded via `/api/notifications`). Cards show module, title, summary, timestamps, and a badge when muted. A project filter menu appears when items reference projects, and the header displays the unread count from `/api/notifications/count`.
* `Pages/Notifications/Index.cshtml.cs` – builds the view model by calling `UserNotificationService` to fetch items, calculate unread counts, and populate project filter options. It also wires in the hub URL for SignalR live updates and exposes API endpoints for mark-read/mark-unread/mute actions.【F:Pages/Notifications/Index.cshtml.cs†L17-L70】【F:Services/Notifications/UserNotificationService.cs†L23-L220】

## Projects
* `Pages/Projects/Index.cshtml` – searchable list of projects ordered by creation time. Supports case-file filtering (ILike match) and surfaces category, HoD and PO assignments next to each entry.
* `Pages/Projects/Create.cshtml` – multi-section form for registering new projects. It lets authors pick a top-level and sub-category, assign HoD/PO roles from pre-filtered lists, capture a unique case file number and optionally seed the last completed stage for in-flight work, with validation against canonical stage codes.
* `Pages/Projects/Overview.cshtml` – the command centre that loads procurement facts, timeline status, plan editor state, assignment options and category breadcrumbs in a single page model. Offcanvas panels reuse this data to edit procurement facts, timeline drafts or project roles, and the procurement panel now only auto-opens when the navigation includes `oc=procurement`, keeping stage-driven refreshes from surfacing stale forms.
* `Pages/Projects/Procurement/Edit.cshtml` – processes procurement edits from the overview offcanvas. It enforces stage completion before accepting IPA/AON/BM/L1/PNC numbers or supply-order dates, wraps writes in a transaction and redirects back to the overview with `oc=procurement` so the editor is reopened only when requested.
* `Pages/Projects/AssignRoles.cshtml` – dedicated handler for updating HoD/PO assignments with concurrency checks on the project row version and full audit logging.
* `Pages/Projects/Timeline/EditPlan.cshtml` – Project Officer/HoD/Admin editor for draft timelines. Supports both exact date entry and auto-generation from durations, prevents edits while a draft is awaiting approval and records audit events describing the chosen action (save vs. submit).
* `Pages/Projects/Timeline/Review.cshtml` – HoD approval workflow. Blocks approvals while procurement backfill is outstanding, captures rejection notes, raises validation errors from `PlanApprovalService` and redirects back to the overview with flash messaging.

## Process designer
* `Pages/Process/Index.cshtml` – presents the currently active stage template version, last-updated timestamp, and a call-to-action to open the checklist editor. Only users with the HoD or MCO role see edit affordances; everyone else can view the version and audit timestamp.【F:Pages/Process/Index.cshtml.cs†L15-L64】
* `/api/processes/{version}/...` endpoints surfaced on the same page power the flow diagrams and checklist management tools described in [docs/timeline.md](timeline.md).

## Settings
* `Pages/Settings/Holidays/Index.cshtml` – Admin and HoD roles can review the holiday calendar that seeds scheduling calculations. Entries are ordered chronologically and stored in `Models/Scheduling/Holiday` for use by the plan generator and snooze presets.【F:Pages/Settings/Holidays/Index.cshtml.cs†L13-L25】【F:Models/Scheduling/Holiday.cs†L1-L8】
