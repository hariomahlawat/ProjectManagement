using System;
using System.Collections.Generic;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskWorkflowPolicy
{
    private readonly ActionTaskPermissionService _permission;

    public ActionTaskWorkflowPolicy(ActionTaskPermissionService permission)
    {
        _permission = permission;
    }

    // SECTION: Supported option lists for task forms and filters.
    public IReadOnlyList<string> AllowedStatusOptions => new[]
    {
        ActionTaskStatuses.Assigned,
        ActionTaskStatuses.InProgress,
        ActionTaskStatuses.Blocked,
        ActionTaskStatuses.Submitted
    };

    public IReadOnlyList<string> PriorityOptions => new[]
    {
        "Low",
        "Normal",
        "High",
        "Critical"
    };

    // SECTION: Action availability guards.
    public bool CanSubmitTask(ActionTaskItem task, string currentUserId)
    {
        return !string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && string.Equals(task.AssignedToUserId, currentUserId, StringComparison.Ordinal);
    }

    public bool CanCloseTask(ActionTaskItem task, string currentRole)
    {
        return _permission.CanClose(currentRole)
            && string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanUpdateTaskStatus(ActionTaskItem task, string currentRole, string currentUserId)
    {
        return !string.Equals(task.Status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && (_permission.CanViewAll(currentRole) || string.Equals(task.AssignedToUserId, currentUserId, StringComparison.Ordinal));
    }

    // SECTION: Transition and remarks validation for command handlers.
    public string? ValidateStatusUpdate(ActionTaskItem task, string targetStatus)
    {
        if (string.Equals(task.Status, targetStatus, StringComparison.OrdinalIgnoreCase))
        {
            return "No status change applied because the selected status is already current.";
        }

        return null;
    }

    public string? ValidateOptionalRemarks(string? remarks)
    {
        if (remarks is null)
        {
            return null;
        }

        return remarks.Length > 4000 ? "Remarks cannot exceed 4000 characters." : null;
    }

    // SECTION: UI style mapping helpers.
    public string GetStatusBadgeClass(string status)
    {
        if (string.Equals(status, ActionTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase)) return "at-badge at-badge-status-progress";
        if (string.Equals(status, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)) return "at-badge at-badge-status-blocked";
        if (string.Equals(status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)) return "at-badge at-badge-status-submitted";
        if (string.Equals(status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)) return "at-badge at-badge-status-closed";
        return "at-badge at-badge-status-assigned";
    }

    public string GetPriorityBadgeClass(string priority)
    {
        if (string.Equals(priority, "Critical", StringComparison.OrdinalIgnoreCase)) return "at-badge at-badge-priority-critical";
        if (string.Equals(priority, "High", StringComparison.OrdinalIgnoreCase)) return "at-badge at-badge-priority-high";
        return "at-badge at-badge-priority-normal";
    }
}
