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
* `Pages/Shared/_TodoWidget.cshtml` – partial responsible for listing, adding, pinning, completing, snoozing, priority changes and deleting tasks without requiring JavaScript. It uses a compact single-line layout with truncated titles, priority dots, due-date chips and a quiet overflow menu rendered in the document body to avoid clipping. The widget is rendered only on the dashboard's right column.

## Tasks
* `Pages/Tasks/Index.cshtml` – full management interface with filter tabs, search and page size selection. Tasks are shown in a grouped list (Overdue / Today / Upcoming / Completed) with priority dots, due-date chips and calm overflow menus. Rows support inline edits, drag-and-drop reordering and a select mode for batch mark-done or delete. Completing a task surfaces an undo toast.
  The quick-add box understands simple tokens like `tomorrow`, `today`, `mon`, `next mon`, `!high` and `!low` to set due dates and priority while stripping them from the final title.
