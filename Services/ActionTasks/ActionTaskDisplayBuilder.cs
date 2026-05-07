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

}
