# PRISM Officer Conference — Phase 3.6

## Scope

Adds direct Action Tracker task assignment from the selected officer's Conference Review page.

## Behaviour

- The **Other Tasks** section always shows an **Add task** action for authorised Comdt/HoD users.
- Task creation is inline; the user remains on the officer's Conference Review page.
- The reviewed officer is fixed as the assignee and is revalidated server-side.
- Required fields are task title, task brief/expected outcome, due date and priority.
- Due dates in the past are rejected using the Action Tracker IST business date.
- The task is created in the existing Action Tasks module with `Assigned` status and no sprint.
- Existing Action Task audit and notification services are reused.
- The task-creation audit records `Created from Officer Conference Review.`
- The assignee receives the normal Action Tracker assignment notification; the creator is excluded by the existing notification pipeline.
- The new task row and both task counters update immediately without page navigation or reload.
- The newly inserted task supports **Add direction** and **Open** immediately.

## Apply

Copy the supplied files into the project root, preserving relative paths. This package is incremental and must be applied after Phase 3.5.

No database migration is required.

## Validation commands

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
node --check wwwroot/js/pages/officer-conference.js
node --test wwwroot/js/pages/officer-conference.test.js
```

## Production verification

1. Open an officer with zero Other Tasks.
2. Select **Add task** and verify that the assignee is fixed to the reviewed officer.
3. Create a task with a future due date.
4. Confirm that the row appears immediately and both task counts change from 0 to 1.
5. Open the task and confirm it appears in Action Tracker with Assigned status, the correct officer, due date and priority.
6. Confirm the assignee receives a notification and the creator does not receive their own notification.
7. Add a conference direction to the newly inserted row without refreshing the page.
8. Verify that a past due date and invalid/blank fields are rejected inline.
