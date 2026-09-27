# Action Tracker development note: Indian Standard Time standard

Action Tracker (`Pages/ActionTasks`, `Services/ActionTasks`) stores instants in UTC and uses the Indian calendar (IST, UTC+05:30, `IstClock`) for everything users see and for date-only business rules. This note records the standard as it is implemented. Code is authoritative.

## Implemented building blocks

| Concern | Implementation |
| --- | --- |
| Clock | `IActionTrackerClock` (`Services/ActionTasks/IActionTrackerClock.cs`), registered as a singleton `SystemActionTrackerClock` in `Program.cs`. Members: `UtcNow`, `UtcToday` (legacy diagnostics only), `IstNow`, `IstToday`, `ConvertUtcToIst`. |
| Display formatting | `Infrastructure/TimeFmt.ToIst(DateTime?)` renders `dd MMM yyyy, HH:mm` plus ` IST`. For example, UTC `2026-04-28 02:08` renders as `28 Apr 2026, 07:38 IST`. It is used in `Details.cshtml` and `_TaskUpdateTimeline.cshtml`. There is **no** `IIndianTimeFormatter`/`IndianTimeFormatter` type; earlier drafts of this note named one. |
| Date-only rules | Overdue, due-today, ageing and "not in the past" validations compare against `IActionTrackerClock.IstToday`. Examples: `ActionTaskService` (due-date change), `ActionTaskQueryService`, `ActionTaskMyWorkQueueBuilder`, `ActionTaskCommandCentreSummaryBuilder`, `ActionTaskSprintWorkspaceSummaryBuilder`, `ActionTaskSprintBoardStateBuilder`, `ActionTaskReportBuilder`, and `Pages/ActionTasks/Index.cshtml.cs` (create/backlog/change-date validation and defaults). |
| Persisted timestamps | Come from `IActionTrackerClock.UtcNow`, as in `ActionSprintService`. |

## Rules for new code
1. Persist instants in UTC, taken from `IActionTrackerClock.UtcNow`.
2. Compare date-only values (due dates, sprint dates, filters) with `IActionTrackerClock.IstToday`. Do not use `UtcToday` or `DateTime.UtcNow.Date`.
3. Render timestamps with `TimeFmt.ToIst` or `IActionTrackerClock.ConvertUtcToIst`. Do not use `.ToLocalTime()`, `DateTime.Now` or `DateTime.Today`.

## Current compliance (grep, 2026-09-27)
- There are no `DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow.Date` or `.ToLocalTime()` calls in `Pages/ActionTasks` or `Services/ActionTasks`.
- There is one remaining direct `DateTime.UtcNow`: `ActionTaskNotificationService` uses it as a fallback when building submitted/closed notification fingerprints (`task.SubmittedOn ?? DateTime.UtcNow`). That service takes the general `IClock` rather than `IActionTrackerClock`. The effect is cosmetic, because the value only feeds a de-duplication key.

## Tests
Action Tracker tests are under `ProjectManagement.Tests/ActionTasks/` (service, query, sprint, notification, permission and page tests). When changing time handling, add or extend a test there that pins a fixed `IActionTrackerClock` and checks the IST boundary. For example, UTC 20:00 falls on the next IST calendar day.
