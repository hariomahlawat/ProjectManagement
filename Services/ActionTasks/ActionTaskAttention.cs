using System;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

/// <summary>Small facts shown beside a task that needs attention, on the IST calendar.</summary>
public static class ActionTaskAttention
{
    /// <summary>
    /// Whole days a submitted task has waited for closure, counted from the IST date it was
    /// submitted; null when the task is not awaiting closure or the submission time is missing.
    /// </summary>
    public static int? DaysAwaitingClosure(ActionTaskItem task, DateTime istToday)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            || task.SubmittedOn is not DateTime submittedUtc)
        {
            return null;
        }

        var submittedIstDate = IstClock.ToIst(submittedUtc).Date;
        return Math.Max(0, (int)(istToday.Date - submittedIstDate).TotalDays);
    }
}
