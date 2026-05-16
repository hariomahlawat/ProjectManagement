# Action Tracker Development Note

## Additional Development Note: Indian Standard Time Display Standard

Action Tracker must use a single, explicit time-handling standard so persisted values remain portable while every operator sees and enters dates in the same business calendar.

### Time storage and display rules

1. **Store timestamps in UTC internally.** Persist created, updated, completed, closed, sprint, audit, report, and workflow timestamps as UTC instants.
2. **Display all Action Tracker user-facing dates/times in Indian Standard Time (IST).** Razor Pages, partial views, dashboard cards, reports, exported text, filter summaries, sprint panels, and validation messages must render Action Tracker date/time values as IST.
3. **Treat date-only inputs as Indian Standard Time calendar dates.** A date submitted without a time component represents that calendar date in IST, not the server's local timezone and not UTC midnight.
4. **Use a central formatter such as `IIndianTimeFormatter` / `IndianTimeFormatter`.** Formatting rules, labels, date-only display, date-time display, and timezone suffixes must live behind the central formatter instead of being duplicated in views or services.
5. **Use a central clock such as `IActionTrackerClock` / `ActionTrackerClock`.** Current-time access for Action Tracker logic must come from the central clock to keep production code, tests, and offline deployments deterministic.
6. **Use `UtcNow` for persisted timestamps and `IndianToday` for overdue, ageing, and validation logic.** Persisted instants should be generated from the clock's UTC value, while business-day comparisons such as overdue buckets, ageing, sprint date validation, and date-only filters should use the clock's IST calendar date.
7. **Avoid direct `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow.Date`, and `.ToLocalTime()` inside Action Tracker views/services.** These APIs can accidentally use the hosting server timezone, truncate UTC before applying IST, or produce inconsistent output between development and the offline deployment environment.

### Required UTC to IST conversion case

The formatter test suite must include the conversion from UTC `2026-04-28 02:08` to IST `28 Apr 2026, 07:38 IST`. This case verifies the fixed `+05:30` offset and the required user-facing label format.

### Implementation review areas

Review the following Action Tracker surfaces when implementing or auditing the IST display standard:

- `Pages/ActionTasks/_TaskDashboard.cshtml`
- `Pages/ActionTasks/_PlanningPlanTab.cshtml`
- `Pages/ActionTasks/_PlanningExecuteTab.cshtml`
- `Pages/ActionTasks/_SprintClosureReview.cshtml`
- `Pages/ActionTasks/_TaskDetails.cshtml`
- `Pages/ActionTasks/_TaskRegister.cshtml`
- `Pages/ActionTasks/_TaskReports.cshtml`
- `Pages/ActionTasks/_TaskMyWork.cshtml`
- `Pages/ActionTasks/_TaskCreatePanel.cshtml`
- `Pages/ActionTasks/Index.cshtml.cs`
- `Services/ActionTasks/ActionTaskReportBuilder.cs`
- `Services/ActionTasks/ActionTaskQueryService.cs`
- `Services/ActionTasks/ActionTaskSprintWorkspaceSummaryBuilder.cs`

### Acceptance criteria

- Action Tracker timestamps continue to be stored and queried as UTC instants internally.
- All Action Tracker user-facing dates/times render in Indian Standard Time through `IIndianTimeFormatter` / `IndianTimeFormatter` or the approved central formatter abstraction.
- Date-only inputs are parsed and validated as IST calendar dates before conversion to persisted UTC values where a timestamp is required.
- Action Tracker views and services use `IActionTrackerClock` / `ActionTrackerClock` or the approved central clock abstraction instead of direct system clock calls.
- Persisted timestamps are assigned from `UtcNow`; overdue, ageing, sprint/day bucketing, and date validation logic use `IndianToday`.
- No Action Tracker view or service introduces direct use of `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow.Date`, or `.ToLocalTime()`.
- Dashboard, planning, sprint closure, details, register, reports, my-work, create-panel, page-model, report-builder, query-service, and sprint-summary areas listed above are reviewed for compliance.
- The UTC `2026-04-28 02:08` value displays as `28 Apr 2026, 07:38 IST`.

### Required tests

- Unit tests for `IndianTimeFormatter` verifying UTC-to-IST date/time display, including UTC `2026-04-28 02:08` -> `28 Apr 2026, 07:38 IST`.
- Unit tests for date-only parsing/normalisation proving submitted dates are interpreted as IST calendar dates.
- Unit tests for `ActionTrackerClock` proving `UtcNow` returns UTC instants and `IndianToday` returns the IST calendar date for a fixed UTC instant.
- Service tests for overdue, ageing, sprint/day bucketing, and validation logic proving they use `IndianToday` instead of UTC date truncation or server-local dates.
- Regression tests or targeted view/service checks proving the implementation review areas do not call `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow.Date`, or `.ToLocalTime()` directly.
- Report/query tests proving report labels, filter summaries, and exported display strings use the central IST formatter.
