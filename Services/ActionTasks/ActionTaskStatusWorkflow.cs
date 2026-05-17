using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

internal static class ActionTaskStatusWorkflow
{
    // SECTION: Canonical status transition targets shared by services and Razor read models.
    public static IReadOnlyList<string> GetAllowedStatusTargets(string currentStatus)
    {
        if (string.Equals(currentStatus, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentStatus, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        return currentStatus switch
        {
            ActionTaskStatuses.Assigned => new[] { ActionTaskStatuses.InProgress, ActionTaskStatuses.Blocked },
            ActionTaskStatuses.InProgress => new[] { ActionTaskStatuses.Blocked },
            ActionTaskStatuses.Blocked => new[] { ActionTaskStatuses.InProgress },
            ActionTaskStatuses.Submitted => Array.Empty<string>(),
            _ => Array.Empty<string>()
        };
    }

    // SECTION: Submit transition is intentionally separate from generic status updates.
    public static bool CanSubmitFromStatus(string currentStatus)
    {
        return string.Equals(currentStatus, ActionTaskStatuses.Assigned, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentStatus, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentStatus, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase);
    }

    // SECTION: Transition guard mirrors the allowed target list and permits only true no-ops.
    public static bool IsAllowedTransition(string currentStatus, string nextStatus)
    {
        if (string.Equals(currentStatus, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(nextStatus, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(currentStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GetAllowedStatusTargets(currentStatus).Contains(nextStatus, StringComparer.OrdinalIgnoreCase);
    }

    // SECTION: Human context is mandatory for operationally important workflow moves.
    public static void ValidateRemarksForStatusTransition(string currentStatus, string nextStatus, string? remarks)
    {
        if (string.Equals(nextStatus, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase) && IsBlank(remarks))
        {
            throw new InvalidOperationException("Remarks are required when marking a task as blocked.");
        }

        if (string.Equals(nextStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase) && IsBlank(remarks))
        {
            throw new InvalidOperationException("Remarks are required when submitting a task.");
        }

        if (string.Equals(currentStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(nextStatus, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && IsBlank(remarks))
        {
            throw new InvalidOperationException("Remarks are required when returning a submitted task for further action.");
        }
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}
