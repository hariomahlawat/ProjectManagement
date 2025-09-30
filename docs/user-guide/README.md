# Project Management Suite — Storyboard User Guide

This guide walks every persona through the day-to-day journeys inside the Project Management Suite. Each section blends narrative "storyboards" with detailed explanations of guardrails, edge cases, and cross-feature integrations so that teams can confidently run projects, calendars, celebrations, and governance tasks.

---

## 1. Personas, permissions, and shared conventions

| Persona | Typical responsibilities | Modules with full edit control | Modules with limited control | View-only access |
| --- | --- | --- | --- | --- |
| **Administrators** | Own tenant configuration, user lifecycle, catalogue and analytics governance | Admin centre (users, catalogues, analytics), Holidays, Celebrations, Calendar, Tasks, Projects | — | All modules |
| **Heads of Department (HoD)** | Manage departmental timelines, approve projects, curate calendars | Calendar, Projects (stage approvals), Holidays | Tasks, Celebrations | Admin analytics (read-only) |
| **Teaching Assistants (TA)** | Coordinate celebrations, task queues, and event logistics | Celebrations, Tasks, Calendar event scheduling | Projects (update supporting docs) | Admin user directory |
| **Project Officers** | Build and track project lifecycles, drive task execution | Projects, Tasks, Calendar | Celebrations | User directory (team only) |
| **Standard Staff** | Consume schedules, manage personal tasks, follow celebrations | Tasks (own scope), subscribe to calendar, mark celebration participation | — | Project dashboards (read-only), audit snapshots |

**Shared conventions**

- **Undo toasts** appear after destructive actions (delete, move, mark done). They stay on screen for ~10 seconds and can be reopened from the notifications bell if dismissed. Undo is powered by soft-deletes and reversible queue messages.
- **UTC-first timestamps**: Inputs accept local time, but back-end normalises to UTC; calendar and task boards auto-adjust to the viewer's locale. When typing natural language dates ("tomorrow", "next wed"), the helper resolves relative to the viewer.
- **Role elevation safeguards**: Users cannot remove their last role that keeps them authenticated; the system blocks saving if that would lock the tenant out.
- **Markdown inputs**: Descriptions in tasks, calendar events, projects, and celebrations accept Markdown which is sanitised before rendering (no raw HTML).

---

## 2. Landing dashboard

**Storyboard**: When Priya (Project Officer) signs in, the dashboard surfaces three widgets: _Today's Tasks_, _Upcoming Calendar_, and _Celebrations_. Priya can tick tasks inline, snooze a follow-up by hovering to reveal quick actions, or jump straight into the project that spawned a task.

- **Widgets & actions**
  - **Todo strip** aggregates the next 10 tasks assigned to the viewer, grouped into _Overdue_, _Today_, _Upcoming_. A quick-add field understands tokens such as `!high` (priority), `#<project code>` (link to project), and natural dates (`next fri 2pm`). Duplicate detection warns if a task with the same title exists in the last 7 days.
  - **Upcoming events** shows the next 30 days. Clicking an entry opens the calendar detail drawer; non-editors see a "Create personal task" button that copies key fields into their task list.
  - **Celebrations carousel** highlights birthdays and work anniversaries. Admins and TAs see an "Plan logistics" CTA which spawns a pre-filled task template.
- **Edge cases**
  - When the viewer has no tasks or events, empty states offer learning links into the user guide and the relevant module for onboarding.
  - If services are offline (detected through heartbeat pings), widgets fall back to cached data with a warning badge.

---

## 3. Tasks hub

**Storyboard**: After meeting notes, Priya captures actions in the Tasks hub. She presses `/` to focus the quick-add bar, types `Draft kickoff deck tomorrow !high #Apollo`, and presses Enter. The task drops into _Today_, pinned automatically because of the `!high` token.

- **Navigation & filters**
  - Tabs: _All_, _Today_, _Upcoming_, _Completed_. Keyboard shortcuts (`1-4`) switch tabs.
  - Search: Full-text search across title, description, linked project, and origin module. Search suggestions show recent queries per user.
  - Pagination: Lists keep 50 items per page; infinite scrolling is available on touch devices.
- **Task lifecycle**
  - **Creation**: Quick-add parses `due:<date>`, `start:<date>`, `@<assignee>` (admin & managers only), and `repeat:<pattern>` (weekly, weekdays, monthly). Validation prevents due dates before start dates and repeats without due dates.
  - **Editing**: Inline editing available on title, due date, priority. Deep edits open a side panel with Markdown preview, linked project selector, reminder options.
  - **Completion**: Checking the box moves the task to _Completed_ with a toast. Undo reopens it. Bulk complete uses shift-click selection.
  - **Snoozing**: Presets for 1h, End of day, Tomorrow, Next week. Custom snooze respects business calendars configured in Holidays. Snoozed tasks surface in Upcoming with a "Snoozed until" badge.
  - **Repeating tasks**: When completing a repeating task, the next occurrence is generated immediately with due date shifts (respecting business days). Edge case: if the next occurrence falls on a weekend and "Skip weekends" is enabled, it jumps to Monday.
- **Collaboration**
  - Comments thread logs updates. Mentions (`@name`) notify participants.
  - Audit log records status changes with timestamp and actor. Admins can export the log for a task.
- **Integrations**
  - From calendar: clicking "Track as task" on an event populates title, due date, and link back to the calendar event. Updates stay in sync for title/time; cancelling the event auto-completes the linked task with a note.
  - From celebrations: "Plan logistics" creates a task with due date = celebration date minus configurable offset (default 3 days).
- **Edge cases & safeguards**
  - Soft-deleted tasks (trash) retain for 30 days; Admins can restore earlier via Admin centre.
  - Concurrency protection prevents double submissions; the UI shows conflict dialogs when another user edits the same task.
  - Attachments are scanned for malware; infected uploads are quarantined and the uploader gets an email.

---

## 4. Calendar workspace

**Storyboard**: Helena (HoD) opens the calendar to schedule a department review. She drags across Wednesday 14:00–15:00 on the week grid, triggering the creation drawer. After setting recurrence (weekly until end of term) and adding agenda Markdown, she assigns Priya as organiser.

- **Views & layout**
  - Modes: _Day_, _Week_, _Month_, _Timeline_. The system remembers the last view per user.
  - Business hours shading highlights 08:00–18:00 (configurable). Off-hours events remain clickable but prompt a confirmation.
  - Category filters (Projects, Academic, Celebrations, Admin) persist in local storage.
- **Event creation & editing**
  - Required fields: title, start, end (enforced end > start), category. Optional: location, attendees, description, reminder.
  - All-day toggle locks times to midnight boundaries. Recurrence supports daily, weekly (with multiple weekdays), monthly (day or ordinal), and yearly patterns. UNTIL date capped at 400 days from start.
  - Validation catches overlaps for organisers, warns but allows double-booking if confirmed.
  - Dragging/resizing disabled for past events and occurrences of a recurring series (edit series or occurrence via dialog instead).
- **Permissions**
  - Admins, HoDs, TAs: full create/edit/delete.
  - Project Officers: create and edit events they own; request changes on others (sends task to owner).
  - Standard Staff: read-only; can copy to personal calendar (ICS download) or spawn tasks.
- **Undo & history**
  - After save or delete, toast shows "Undo" which reverts the last change. Undo history resets when navigating away.
  - Event history panel lists previous times/locations with actor stamps (Admins only).
- **Integrations**
  - **Tasks**: as above.
  - **Projects**: events linked to project stages show stage badges. Moving the event prompts to adjust project stage dates.
  - **Celebrations**: celebrations appear as read-only events sourced from the celebrations registry.
- **Edge cases**
  - Leap-day handling ensures yearly recurring events on Feb 29th shift to Feb 28th on non-leap years, unless "Strict" mode is enabled (Admin setting) which keeps them on Feb 29th and drops the year without the date.
  - Time-zone shifts: when organisers travel, a banner warns about differing base time zones and offers to convert.
  - API returns `422` for ranges beyond 400 days and `403` for insufficient permissions; UI surfaces these as inline errors.

---

## 5. Celebrations registry

**Storyboard**: Omar (TA) prepares monthly celebrations. He filters the registry to "Work anniversaries" and sees upcoming dates. For Fatima's 5-year mark, he edits the record to attach a note and triggers "Plan logistics" which opens a task template.

- **Catalogue & filters**
  - Filter pills for _Birthdays_, _Work anniversaries_, _Milestones_. Date range selector supports relative windows (next 7 / 30 / 90 days) and custom ranges.
  - Search queries first/last name, department, tags. Results show next occurrence and age/tenure calculations.
- **Record lifecycle**
  - **Create**: Requires name, celebration type, date (day/month/year optional for birthdays). Validation prevents invalid dates (e.g., Feb 30). Leap-year birthdays default to Feb 29.
  - **Edit**: Admins and TAs can update; history stored for auditing.
  - **Soft delete**: Moves records to the archive for 30 days with undo. Hard delete possible via Admin centre.
- **Edge cases**
  - Incomplete birthdays (no year) display age as "—" but still calculate next occurrence.
  - Duplicate detection compares name + date; duplicates require confirmation.
- **Integrations & exports**
  - Creating tasks from celebrations sets due date offset. Updates to the celebration push notifications to assignees.
  - Export CSV respects active filters; includes calculated next occurrence and last editor.

---

## 6. Projects module

**Storyboard**: Priya manages the Apollo project. She advances the project from _Initiation_ to _Planning_ after completing intake tasks. Stage change opens a modal summarising required documentation; Priya uploads the risk register and confirms.

- **Lifecycle overview**
  - Stages follow the template (Initiation → Planning → Execution → Monitoring → Closure). Each stage enforces required artefacts via checklists.
  - Stage gates require approvals: Initiation (HoD), Planning (Admin or delegated approver), Execution (Project Board), Closure (Admin).
- **Boards & timelines**
  - Kanban board visualises work packages with swimlanes per stage. Dragging cards respects dependencies; blocked moves prompt reasons.
  - Timeline view overlays milestones with calendar events; clicking a milestone opens the linked event.
- **Documents & collaboration**
  - File uploads scanned and versioned. Latest version pinned; previous versions accessible via history.
  - Comments support mentions and decision logging. Decisions marked "Critical" trigger notifications to HoD.
- **Edge cases**
  - When a project misses a mandatory artefact, progression button disables with tooltip listing outstanding items.
  - Closing a project schedules an automatic archive job (48h delay) that finalises tasks and removes calendar events.
- **Supporting references**
  - Cross-link to `[docs/projects-module.md](../projects-module.md)` for deep architecture narrative and process details.

---

## 7. Admin & governance centre

**Storyboard**: Aisha (Administrator) reviews access. She opens the Admin centre, filters active users, and disables an account pending a departure. The system warns if she attempts to disable the last active admin.

- **User management**
  - Create: enter email, name, assign roles (Admin, HoD, TA, Project Officer, Staff). Optionally send invite email.
  - Edit: update roles, reset MFA, trigger password reset, regenerate security stamp. System blocks removing your own last admin role.
  - Disable/Enable: toggles sign-in ability while retaining history. Disabled users flagged across modules.
  - Deletion: hard delete requires two-person approval; soft delete keeps data for 30 days.
- **Role & permission safeguards**
  - Visual matrix shows module rights. Hover tooltips summarise restrictions.
  - Attempting to assign conflicting roles prompts guidance (e.g., a user cannot be both External Reviewer and Admin).
- **Analytics**
  - Dashboards show login heatmaps, odd-hour sign-ins (outside configured business hours). Filters for time window, role, department. CSV export limited to 10k rows.
  - Alerts: Spike detection warns Admins via email; rate thresholds configurable.
- **Audit logs**
  - Filter by actor, module, action type, date range. Exports respect filters and stream to background job.
  - Edge case: if export exceeds 5 minutes, job retries and notifies Admin with partial results.
- **Catalogue management**
  - Project categories: tree editing with drag-and-drop. Prevents creating circular references. Inactive categories hidden from selectors but preserved historically.
  - Lookup tables (departments, sponsor units): in-line editing with validation. Changes logged with before/after snapshots.

---

## 8. Settings & reference data

**Storyboard**: Helena configures the academic calendar. She adds public holidays and sets blackout periods so scheduling respects them.

- **Holidays & blackout dates**
  - Accessible to Admins and HoDs. Requires name, date range, applicable departments. Duplicates blocked.
  - Tasks and calendar respect these dates by default (snoozed tasks land on the next working day; calendar suggests alternative slots).
- **Notification templates**
  - Admins edit email/SMS templates with merge fields (`{{UserName}}`, `{{TaskTitle}}`). Preview renders sample data. Publishing logs version history.
- **Integrations**
  - Settings feed into plan generators, escalation SLA timers, and reporting filters.

---

## 9. Onboarding checklists

- New Admins: review persona matrix, configure catalogue, import holidays, audit roles.
- HoDs: verify project stage templates, review calendar permissions, set department holidays.
- TAs: populate celebrations registry, subscribe to calendars, learn quick-add tokens.
- Project Officers: read the Projects module storyboard, configure personal task views, practice converting calendar events to tasks.
- Standard Staff: customise dashboard widgets, learn keyboard shortcuts, set notification preferences.

---

## 10. Glossary & quick reference

| Term | Definition |
| --- | --- |
| **Undo toast** | Temporary banner allowing reversal of the last destructive action. |
| **Soft delete** | Reversible removal that hides an item but keeps it for recovery. |
| **Stage gate** | Checkpoint that requires approvals and artefacts before progressing a project. |
| **Task quick-add tokens** | Shorthand commands in the task creation input (`!priority`, `@assignee`, `#project`). |
| **Business hours shading** | Visual indicator on calendars showing preferred scheduling windows. |

---

## 11. Further reading

- [Projects module storyboard](../projects-module.md)
- [Architecture overview](../architecture.md)
- [Extending the platform](../extending.md)

