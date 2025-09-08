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
* `Pages/Shared/_TodoWidget.cshtml` – partial responsible for listing, adding, pinning, completing, snoozing, quick priority changes and deleting tasks without requiring JavaScript, with priority dots and due badges. It is also rendered inside a navbar off-canvas accessible from a bell icon showing the count of urgent tasks.

## Tasks
* `Pages/Tasks/Index.cshtml` – full management interface with tabs, search, page size selection, pagination, batch mark-done/delete actions, add, inline edits for title, notes, priority, due date, pinning, mark done, delete and drag-and-drop reordering of open tasks.
