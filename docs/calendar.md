# Calendar Module

Provides organisation-wide calendar with month, week and list views using FullCalendar.

## API Endpoints

- `GET /calendar/events?start=ISO&end=ISO` – returns events in the supplied range.
- `GET /calendar/events/{id}` – full event details including rendered description.
- `POST /calendar/events` – create event (Admin/TA/HOD).
- `PUT /calendar/events/{id}` – update event (Admin/TA/HOD).
- `DELETE /calendar/events/{id}` – soft delete (Admin/TA/HOD).
- `POST /calendar/events/{id}/task` – add a task due at the event start.

## UI

The `/Calendar` page shows the interactive calendar. Editors (Admin/TA/HOD) can create, drag and resize events and manage them through an offcanvas form. All users can view details and add events to their personal task list.

A dashboard widget lists the next five upcoming events with a link to the full calendar.
