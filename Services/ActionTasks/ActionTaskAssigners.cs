using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

/// <summary>
/// Who gave each task to its current assignee. The creator assigns a task created with an
/// assignee; a backlog item is assigned when it is put into a sprint with a responsible person
/// (TaskAssignedToSprint), and a reassignment changes the assignee (TaskReassigned). The latest
/// of those audit entries wins; without one, the creator assigned it.
/// </summary>
public static class ActionTaskAssigners
{
    public const string AssignedToSprintAction = "TaskAssignedToSprint";
    public const string ReassignedAction = "TaskReassigned";

    /// <summary>Task id to the user id of whoever assigned it to its current assignee.</summary>
    public static Task<IReadOnlyDictionary<int, string>> ResolveAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<ActionTaskItem> tasks,
        CancellationToken cancellationToken = default)
        => ResolveAsync(db, (tasks ?? Array.Empty<ActionTaskItem>()).Select(task => (task.Id, task.CreatedByUserId)).ToList(), cancellationToken);

    /// <summary>As above, from each task's id and creator.</summary>
    public static async Task<IReadOnlyDictionary<int, string>> ResolveAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<(int TaskId, string CreatedByUserId)> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (tasks is null || tasks.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var ids = tasks.Select(task => task.TaskId).Distinct().ToArray();
        var assignments = await db.ActionTaskAuditLogs
            .AsNoTracking()
            .Where(log => ids.Contains(log.TaskId)
                && (log.ActionType == AssignedToSprintAction || log.ActionType == ReassignedAction))
            .Select(log => new { log.TaskId, log.PerformedAt, log.Id, log.PerformedByUserId })
            .ToListAsync(cancellationToken);
        var latest = assignments
            .GroupBy(log => log.TaskId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(log => log.PerformedAt).ThenByDescending(log => log.Id).First().PerformedByUserId);

        var result = new Dictionary<int, string>(ids.Length);
        foreach (var task in tasks)
        {
            var assigner = latest.TryGetValue(task.TaskId, out var actor) && !string.IsNullOrWhiteSpace(actor)
                ? actor
                : task.CreatedByUserId;
            if (!string.IsNullOrWhiteSpace(assigner))
            {
                result[task.TaskId] = assigner;
            }
        }

        return result;
    }
}
