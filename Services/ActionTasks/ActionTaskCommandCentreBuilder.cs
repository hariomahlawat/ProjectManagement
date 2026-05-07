using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskCommandCentreBuilder
{
    private readonly IActionTrackerClock _clock;

    public ActionTaskCommandCentreBuilder(IActionTrackerClock clock)
    {
        _clock = clock;
    }

    // SECTION: Compose Command Centre KPI text and counts outside the Razor Page model.
    public ActionTaskCommandCentreSummary Build(IReadOnlyList<ActionTaskItem> tasks, int criticalOpenCount, int carryForwardCandidateTasks)
    {
        var activeCount = tasks.Count(IsOpenTask);
        var overdueCount = tasks.Count(IsOverdue);
        var blockedCount = CountByStatus(tasks, ActionTaskStatuses.Blocked);
        var submittedPendingClosureCount = CountByStatus(tasks, ActionTaskStatuses.Submitted);
        var activeCriticalCount = tasks.Count(t => IsOpenTask(t) && string.Equals(t.Priority, "Critical", StringComparison.OrdinalIgnoreCase));

        return new ActionTaskCommandCentreSummary(
            ActiveCount: activeCount,
            OverdueCount: overdueCount,
            BlockedCount: blockedCount,
            CriticalOpenCount: criticalOpenCount,
            ActiveCriticalCount: activeCriticalCount,
            SubmittedPendingClosureCount: submittedPendingClosureCount,
            DashboardCommandFocusSummary: BuildDashboardCommandFocusSummary(overdueCount, blockedCount, submittedPendingClosureCount, criticalOpenCount, carryForwardCandidateTasks),
            CommandSummary: BuildCommandSummary(activeCount, activeCriticalCount, overdueCount, submittedPendingClosureCount));
    }

    // SECTION: Existing Command Centre focus summary wording is preserved for stable dashboard UX.
    private static string BuildDashboardCommandFocusSummary(int overdueCount, int blockedCount, int submittedPendingClosureCount, int criticalOpenCount, int carryForwardCandidateTasks)
        => $"{overdueCount} overdue. {blockedCount} blocked. {submittedPendingClosureCount} submitted pending closure. {criticalOpenCount} critical open. {carryForwardCandidateTasks} carry-forward candidates.";

    // SECTION: Existing Command Centre narrative wording is preserved for stable dashboard UX.
    private static string BuildCommandSummary(int activeCount, int activeCriticalCount, int overdueCount, int submittedPendingClosureCount)
    {
        var activeTasksText = $"There {(activeCount == 1 ? "is" : "are")} {activeCount} active {(activeCount == 1 ? "task" : "tasks")}";
        var criticalText = $", including {activeCriticalCount} critical {(activeCriticalCount == 1 ? "task" : "tasks")}.";
        var overdueText = overdueCount switch
        {
            0 => "No tasks are overdue.",
            1 => "1 task is overdue.",
            _ => $"{overdueCount} tasks are overdue."
        };

        var submittedPendingText = submittedPendingClosureCount switch
        {
            0 => "No submitted task is pending closure.",
            1 => "1 submitted task is pending closure.",
            _ => $"{submittedPendingClosureCount} submitted tasks are pending closure."
        };

        return $"{activeTasksText}{criticalText} {overdueText} {submittedPendingText}";
    }

    // SECTION: Date-sensitive Command Centre checks use the Action Tracker clock consistently.
    private bool IsOverdue(ActionTaskItem task) => IsOpenTask(task) && task.DueDate.Date < _clock.UtcToday;
    private static bool IsOpenTask(ActionTaskItem task) => !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);
    private static int CountByStatus(IReadOnlyList<ActionTaskItem> tasks, string status) => tasks.Count(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase));
}

public sealed record ActionTaskCommandCentreSummary(
    int ActiveCount,
    int OverdueCount,
    int BlockedCount,
    int CriticalOpenCount,
    int ActiveCriticalCount,
    int SubmittedPendingClosureCount,
    string DashboardCommandFocusSummary,
    string CommandSummary)
{
    public static ActionTaskCommandCentreSummary Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        "0 overdue. 0 blocked. 0 submitted pending closure. 0 critical open. 0 carry-forward candidates.",
        "There are 0 active tasks, including 0 critical tasks. No tasks are overdue. No submitted task is pending closure.");
}
