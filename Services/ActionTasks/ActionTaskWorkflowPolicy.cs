using System;
using System.Collections.Generic;
using ProjectManagement.Configuration;
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
        ActionTaskStatuses.Blocked
    };

    public IReadOnlyList<string> GetAllowedStatusTargets(ActionTaskItem task, string currentRole, string currentUserId)
    {
        if (!CanUpdateTaskStatus(task, currentRole, currentUserId))
        {
            return Array.Empty<string>();
        }

        return ActionTaskStatusWorkflow.GetAllowedStatusTargets(task.Status);
    }

    public IReadOnlyList<string> PriorityOptions => new[]
    {
        "Low",
        "Normal",
        "High",
        "Critical"
    };

    // SECTION: Role/state interaction projection shared by Peek and full task workspace.
    // Submitted is an approval boundary. Human remarks remain available, but task
    // definition/planning is frozen until Command accepts or returns the task.
    public ActionTaskInteractionCapabilities GetInteractionCapabilities(
        ActionTaskItem task,
        string currentRole,
        string currentUserId)
    {
        var isClosed = IsStatus(task, ActionTaskStatuses.Closed);
        var isBacklog = IsStatus(task, ActionTaskStatuses.Backlog);
        var isAssigned = IsStatus(task, ActionTaskStatuses.Assigned);
        var isInProgress = IsStatus(task, ActionTaskStatuses.InProgress);
        var isBlocked = IsStatus(task, ActionTaskStatuses.Blocked);
        var isSubmitted = IsStatus(task, ActionTaskStatuses.Submitted);
        var isAssignedUser = !string.IsNullOrWhiteSpace(currentUserId)
            && string.Equals(task.AssignedToUserId, currentUserId, StringComparison.Ordinal);
        var isPlanningAuthority = _permission.CanViewAll(currentRole);
        var canUpdateStatus = CanUpdateTaskStatus(task, currentRole, currentUserId);
        var metadataMutable = !isClosed && !isSubmitted;
        var canManagePlanning = metadataMutable && _permission.CanManageSprints(currentRole);

        return new ActionTaskInteractionCapabilities(
            IsAssignedUser: isAssignedUser,
            IsPlanningAuthority: isPlanningAuthority,
            CanAddRemark: !isClosed && _permission.CanAddTaskUpdate(currentRole, currentUserId, task.AssignedToUserId),
            CanAddConferenceRemark: !isClosed && _permission.CanAddConferenceUpdate(currentRole),
            CanStartWork: isAssigned && isAssignedUser && canUpdateStatus,
            CanResumeWork: isBlocked && isAssignedUser && canUpdateStatus,
            CanSubmitForClosure: isInProgress && CanSubmitTask(task, currentUserId),
            CanBlockAsOwner: (isAssigned || isInProgress) && isAssignedUser && canUpdateStatus,
            CanBlockAsCommandControl: (isAssigned || isInProgress) && !isAssignedUser && isPlanningAuthority && canUpdateStatus,
            CanResumeAsCommandControl: isBlocked && !isAssignedUser && isPlanningAuthority && canUpdateStatus,
            CanAcceptAndClose: isSubmitted && _permission.CanCloseTaskDirectly(task, currentRole),
            CanReturnForAction: isSubmitted && CanReturnTaskForAction(task, currentRole),
            CanChangeDate: metadataMutable && CanChangeTaskDate(task, currentRole),
            CanEditTaskDetails: metadataMutable && _permission.CanEditTaskDetails(currentRole),
            CanReassignTask: metadataMutable && _permission.CanReassignTask(task, currentRole),
            CanChangePriority: metadataMutable && _permission.CanChangeTaskPriority(task, currentRole),
            CanManagePlanning: canManagePlanning,
            CanAssignBacklogToSprint: canManagePlanning && isBacklog,
            CanAddToSprint: canManagePlanning && !isBacklog && !task.SprintId.HasValue,
            CanRemoveFromSprint: canManagePlanning && task.SprintId.HasValue,
            CanMoveToBacklog: canManagePlanning && !isBacklog,
            CanCloseDirectly: metadataMutable && _permission.CanCloseTaskDirectly(task, currentRole),
            CanViewSystemHistory: CanViewSystemHistory(currentRole));
    }

    // SECTION: Action availability guards.
    public bool CanSubmitTask(ActionTaskItem task, string currentUserId)
    {
        return !IsStatus(task, ActionTaskStatuses.Backlog)
            && !IsStatus(task, ActionTaskStatuses.Submitted)
            && !IsStatus(task, ActionTaskStatuses.Closed)
            && string.Equals(task.AssignedToUserId, currentUserId, StringComparison.Ordinal);
    }

    public bool CanCloseTask(ActionTaskItem task, string currentRole)
        => _permission.CanCloseTaskDirectly(task, currentRole);

    public bool CanReturnTaskForAction(ActionTaskItem task, string currentRole)
        => _permission.CanReturnTaskForAction(task, currentRole);

    public bool CanUpdateTaskStatus(ActionTaskItem task, string currentRole, string currentUserId)
    {
        return !IsStatus(task, ActionTaskStatuses.Backlog)
            && !IsStatus(task, ActionTaskStatuses.Submitted)
            && !IsStatus(task, ActionTaskStatuses.Closed)
            && (_permission.CanViewAll(currentRole) || string.Equals(task.AssignedToUserId, currentUserId, StringComparison.Ordinal));
    }

    public bool CanChangeTaskDate(ActionTaskItem task, string currentRole)
    {
        return _permission.CanChangeTaskDate(currentRole)
            && !IsStatus(task, ActionTaskStatuses.Submitted)
            && !IsStatus(task, ActionTaskStatuses.Closed);
    }

    public bool CanViewSystemHistory(string currentRole)
    {
        return string.Equals(currentRole, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentRole, RoleNames.HoD, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentRole, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
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
        if (string.Equals(status, ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase)) return "at-badge at-badge-status-backlog";
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

    private static bool IsStatus(ActionTaskItem task, string status)
        => string.Equals(task.Status, status, StringComparison.OrdinalIgnoreCase);
}
