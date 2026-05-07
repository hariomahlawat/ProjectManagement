using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskCommandCentreSummaryBuilder
{
    private readonly IActionTrackerClock _clock;

    public ActionTaskCommandCentreSummaryBuilder(IActionTrackerClock clock)
    {
        _clock = clock;
    }

    // SECTION: Command Centre summary projection centralizes dashboard KPI and narrative composition.
    public ActionTaskCommandCentreSummary Build(IReadOnlyList<ActionTaskItem> tasks, int criticalOpenCount, int carryForwardCandidateTasks)
    {
        var activeCount = tasks.Count(IsOpenTask);
        var overdueCount = tasks.Count(IsTaskOverdue);
        var activeCriticalCount = tasks.Count(t => IsOpenTask(t) && string.Equals(t.Priority, "Critical", StringComparison.OrdinalIgnoreCase));
        var blockedCount = CountByStatus(tasks, ActionTaskStatuses.Blocked);
        var submittedPendingClosureCount = CountByStatus(tasks, ActionTaskStatuses.Submitted);

        return new ActionTaskCommandCentreSummary(
            activeCount,
            overdueCount,
            CountByStatus(tasks, ActionTaskStatuses.InProgress),
            submittedPendingClosureCount,
            blockedCount,
            CountByStatus(tasks, ActionTaskStatuses.Closed),
            activeCriticalCount,
            BuildCommandFocusSummary(overdueCount, blockedCount, submittedPendingClosureCount, criticalOpenCount, carryForwardCandidateTasks),
            BuildCommandSummary(activeCount, activeCriticalCount, overdueCount, submittedPendingClosureCount),
            BuildTopAttentionItems(tasks));
    }

    // SECTION: Report top-attention prioritization keeps urgent overdue and critical work first.
    private IReadOnlyList<ActionTaskItem> BuildTopAttentionItems(IReadOnlyList<ActionTaskItem> tasks)
    {
        var today = _clock.UtcToday;
        return tasks
            .Where(IsOpenTask)
            .Select(t => new
            {
                Task = t,
                Score = GetAttentionScore(t, today)
            })
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Task.DueDate)
            .ThenBy(x => x.Task.Id)
            .Take(3)
            .Select(x => x.Task)
            .ToList();
    }

    private static string BuildCommandFocusSummary(int overdueCount, int blockedCount, int submittedPendingClosureCount, int criticalOpenCount, int carryForwardCandidateTasks)
        => $"{overdueCount} overdue. {blockedCount} blocked. {submittedPendingClosureCount} submitted pending closure. {criticalOpenCount} critical open. {carryForwardCandidateTasks} carry-forward candidates.";

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

    private bool IsTaskOverdue(ActionTaskItem task) => IsOpenTask(task) && task.DueDate.Date < _clock.UtcToday;

    private static int CountByStatus(IReadOnlyList<ActionTaskItem> tasks, string status)
        => tasks.Count(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase));

    private static int GetAttentionScore(ActionTaskItem task, DateTime today)
    {
        var isOverdue = task.DueDate.Date < today;
        var isCritical = string.Equals(task.Priority, "Critical", StringComparison.OrdinalIgnoreCase);
        if (isOverdue && isCritical) return 1;
        if (isOverdue) return 2;
        if (string.Equals(task.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase) && isCritical) return 3;
        if (isCritical) return 4;
        if (string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)) return 5;
        return 6;
    }

    private static bool IsOpenTask(ActionTaskItem task)
        => !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);
}

public sealed record ActionTaskCommandCentreSummary(
    int ActiveCount,
    int OverdueCount,
    int InProgressCount,
    int SubmittedCount,
    int BlockedCount,
    int ClosedCount,
    int ActiveCriticalCount,
    string DashboardCommandFocusSummary,
    string CommandSummary,
    IReadOnlyList<ActionTaskItem> TopAttentionTasks)
{
    public static ActionTaskCommandCentreSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, "0 overdue. 0 blocked. 0 submitted pending closure. 0 critical open. 0 carry-forward candidates.", "There are 0 active tasks, including 0 critical tasks. No tasks are overdue. No submitted task is pending closure.", Array.Empty<ActionTaskItem>());
}
