# Project Management

This project is an ASP.NET Core application that provides tools for managing users, tasks, celebrations, and calendar events.

## Modules and Features

### Admin & User Management
- Create users with full name, rank, initial password, and multiple roles while logging the action and forcing a password change on first login.
- Update user details and roles with safeguards that prevent removing or deleting the last active admin and block self-deletion.
- Enable or disable accounts, including hard delete requests with an undo window and audit logs.
- Reset passwords, update security stamps, and log lifecycle actions for accountability.

### Tasks
- Personal todo items support priorities, due dates, pinning, and ordering, with widget statistics for overdue and due-today counts.
- Create, edit, toggle completion, pin or unpin, reorder items, clear completed tasks, and perform bulk mark-done or delete operations.

### Celebrations
- Track birthdays and anniversaries with optional spouse name and year.
- List upcoming celebrations with search, type filters, and time windows while computing days away and allowing soft deletion.
- Admin and TA roles can add or edit entries with validation for dates.

### Calendar
- Manage events with categories, locations, all-day or timed spans, and optional recurrence rules.
- Interactive calendar supports drag-and-drop, resizing, category filtering, undo, and a read-only view with the ability to add events to tasks.

## Documentation
Additional technical documentation is available in the [docs](docs) directory.
