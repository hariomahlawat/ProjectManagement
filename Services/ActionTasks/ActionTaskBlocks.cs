using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

/// <summary>Why a task is blocked and since when: the remarks and time of its latest move to Blocked.</summary>
public sealed record ActionTaskBlock(string? Reason, DateTime BlockedAtUtc);

/// <summary>
/// The latest move to Blocked for each task, read from the task audit log. One rule for every
/// view that shows a blocking reason (the Command desk, Task Management's Needs attention).
/// </summary>
public static class ActionTaskBlocks
{
    public static async Task<IReadOnlyDictionary<int, ActionTaskBlock>> LatestAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<int> taskIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (taskIds is null || taskIds.Count == 0)
        {
            return new Dictionary<int, ActionTaskBlock>();
        }

        var ids = taskIds.Distinct().ToArray();
        var moves = await db.ActionTaskAuditLogs
            .AsNoTracking()
            .Where(log => ids.Contains(log.TaskId)
                && log.ActionType == "StatusUpdated"
                && log.NewValue == ActionTaskStatuses.Blocked)
            .Select(log => new { log.TaskId, log.PerformedAt, log.Id, log.Remarks })
            .ToListAsync(cancellationToken);

        return moves
            .GroupBy(log => log.TaskId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderByDescending(log => log.PerformedAt).ThenByDescending(log => log.Id).First();
                    return new ActionTaskBlock(
                        string.IsNullOrWhiteSpace(latest.Remarks) ? null : latest.Remarks.Trim(),
                        DateTime.SpecifyKind(latest.PerformedAt, DateTimeKind.Utc));
                });
    }
}
