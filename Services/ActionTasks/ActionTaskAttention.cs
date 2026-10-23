using System;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

/// <summary>Small facts shown beside a task that needs attention, on the IST calendar.</summary>
public static class ActionTaskAttention
{
    /// <summary>
    /// Days between the task's due date and the IST date (positive when past due). The one day
    /// count for overdue work: Reports' overdue ageing and Task Management's "N days overdue".
    /// Whether a task counts as overdue at all is decided by the caller's overdue rule.
    /// </summary>
    public static int DaysPastDue(ActionTaskItem task, DateTime istToday)
    {
        ArgumentNullException.ThrowIfNull(task);
        return (int)(istToday.Date - task.DueDate.Date).TotalDays;
    }

    /// <summary>
    /// Whole days a submitted task has waited for closure, counted from the IST date it was
    /// submitted (its assignment date when the submission time is missing); null when the task
    /// is not awaiting closure. Reports' pending-closure ageing uses the same count.
    /// </summary>
    public static int? DaysAwaitingClosure(ActionTaskItem task, DateTime istToday)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var submittedIstDate = IstClock.ToIst(task.SubmittedOn ?? task.AssignedOn).Date;
        return Math.Max(0, (int)(istToday.Date - submittedIstDate).TotalDays);
    }
}
