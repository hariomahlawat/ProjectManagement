using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskDisplayBuilder
{
    private readonly IActionTrackerClock _clock;

    public ActionTaskDisplayBuilder(IActionTrackerClock clock)
    {
        _clock = clock;
    }

    // SECTION: Priority-based My Work queue assigns each task to exactly one primary section.
    public IReadOnlyList<ActionTaskItem> BuildMyWorkQueueSection(IReadOnlyList<ActionTaskItem> tasks, ActionTaskMyWorkQueueSection section)
        => tasks
            .Select(task => new { Task = task, Section = ResolveMyWorkQueueSection(task) })
            .Where(item => item.Section == section)
            .Select(item => item.Task)
            .OrderBy(ResolveMyWorkPriorityRank)
            .ThenBy(task => StatusOrder(task.Status))
            .ThenBy(task => task.DueDate)
            .ThenBy(task => task.Id)
            .ToList();

    // SECTION: Report top-attention prioritization is reusable outside the page model.
    public IReadOnlyList<ActionTaskItem> BuildTopAttentionItems(IReadOnlyList<ActionTaskItem> tasks)
    {
        var today = _clock.UtcToday;
        return tasks
            .Where(t => !string.Equals(t.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
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

    // SECTION: My Work precedence prevents repeated cards across action, execution, submitted, and remaining sections.
    private ActionTaskMyWorkQueueSection ResolveMyWorkQueueSection(ActionTaskItem task)
    {
        if (IsTaskOverdue(task) || IsTaskDueToday(task) || IsTaskBlocked(task))
        {
            return ActionTaskMyWorkQueueSection.ActionRequired;
        }

        if (IsTaskInProgress(task))
        {
            return ActionTaskMyWorkQueueSection.CurrentWork;
        }

        if (IsTaskSubmitted(task))
        {
            return ActionTaskMyWorkQueueSection.SubmittedAwaitingClosure;
        }

        return ActionTaskMyWorkQueueSection.AllMyTasks;
    }

    // SECTION: My Work row ordering follows the required urgency precedence inside compact sections.
    private int ResolveMyWorkPriorityRank(ActionTaskItem task)
    {
        if (IsTaskOverdue(task)) return 1;
        if (IsTaskDueToday(task)) return 2;
        if (IsTaskBlocked(task)) return 3;
        if (IsTaskInProgress(task)) return 4;
        if (IsTaskSubmitted(task)) return 5;
        return 6;
    }

    // SECTION: Report top-attention score keeps urgent overdue and critical work first.
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

    // SECTION: My Work status predicates keep grouping readable without changing workflow rules.
    private bool IsTaskOverdue(ActionTaskItem task) => IsOpenTask(task) && task.DueDate.Date < _clock.UtcToday;
    private bool IsTaskDueToday(ActionTaskItem task) => IsOpenTask(task) && task.DueDate.Date == _clock.UtcToday;
    private static bool IsTaskBlocked(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase);
    private static bool IsTaskInProgress(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase);
    private static bool IsTaskSubmitted(ActionTaskItem task) => string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
    private static bool IsOpenTask(ActionTaskItem task) => !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase);

    // SECTION: Reusable status ordering mirrors page-level operational list ordering.
    private static int StatusOrder(string status) => status switch
    {
        ActionTaskStatuses.Assigned => 1,
        ActionTaskStatuses.InProgress => 2,
        ActionTaskStatuses.Blocked => 3,
        ActionTaskStatuses.Submitted => 4,
        ActionTaskStatuses.Closed => 5,
        _ => 99
    };
}

public enum ActionTaskMyWorkQueueSection
{
    ActionRequired,
    CurrentWork,
    SubmittedAwaitingClosure,
    AllMyTasks
}
