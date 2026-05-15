using System;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public static class ActionTaskBucketInvariantValidator
{
    // SECTION: Central workflow bucket invariant validation for backlog, outside-sprint, sprint, and closed tasks.
    public static void ValidateTaskBucketInvariant(ActionTaskItem task)
    {
        var hasAssignee = !string.IsNullOrWhiteSpace(task.AssignedToUserId);
        var hasSprint = task.SprintId.HasValue;

        if (string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase) && hasAssignee)
        {
            throw new InvalidOperationException("Backlog tasks cannot have a responsible person.");
        }

        if (string.Equals(task.Status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase) && hasSprint)
        {
            throw new InvalidOperationException("Backlog tasks cannot be assigned to a sprint.");
        }

        if (hasSprint && !hasAssignee)
        {
            throw new InvalidOperationException("Sprint tasks must have a responsible person.");
        }

        if (IsAssignedExecutionStatus(task.Status) && !hasAssignee)
        {
            throw new InvalidOperationException("Assigned, in-progress, blocked, and submitted tasks must have a responsible person.");
        }

        if (string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase) && task.ClosedOn is null)
        {
            throw new InvalidOperationException("Closed tasks must have a closure timestamp.");
        }
    }

    // SECTION: Execution statuses are valid only for assigned work, not true backlog items.
    private static bool IsAssignedExecutionStatus(string status)
        => string.Equals(status, ActionTaskStatuses.Assigned, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
}
