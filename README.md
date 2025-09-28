# Project Management

This project is a .NET 8 ASP.NET Core application that provides tools for managing users, tasks, celebrations, and calendar events.

## Modules and Features

### Admin & User Management
- Create users with full name, rank, initial password, and multiple roles while logging the action and forcing a password change on first login.
- Update user details and roles with safeguards that prevent removing or deleting the last active admin and block self-deletion.
- Enable or disable accounts, including hard delete requests with an undo window, an automatic purge worker and audit logs.
- Reset passwords, update security stamps, and log lifecycle actions for accountability.
- Record login events and visualise activity with scatter-chart analytics.

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

### Projects
- Register projects with categories, lead roles and case file metadata so procurement and execution history can be managed in one place.
- The overview workspace aggregates procurement facts, timeline progress, approval history and outstanding backfill/plan actions.
- Procurement officers capture IPA, AON, benchmark, L1, PNC and supply-order milestones with audit logging and stage gating.
- Delivery teams collaborate through threaded comments with role-based editing, pinned updates and secure document attachments.

## Documentation
Additional technical documentation is available in the [docs](docs) directory.

## Development guardrails
- Inline styles and script handlers are forbidden in Razor views. Run `./tools/check-views.sh` before committing to ensure no `<script>` tags without `src`, `on*=` attributes, or `style="..."` slips into `.cshtml`/`.html` files.
