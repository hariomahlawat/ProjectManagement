using System;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

/// <summary>
/// Human-facing labels for the task timeline and audit trail. Keeps raw model
/// terminology out of the UI without changing persisted history.
/// </summary>
public static class ActionTaskPresentation
{
    public static string UpdateTypeLabel(string? updateType)
    {
        if (string.Equals(updateType, ActionTaskUpdateTypes.Conference, StringComparison.OrdinalIgnoreCase)) return "Conference";
        if (string.Equals(updateType, ActionTaskUpdateTypes.Comment, StringComparison.OrdinalIgnoreCase)) return "General";
        return "Progress";
    }

    public static string RoleLabel(string? role)
    {
        if (string.Equals(role, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase)) return "Comdt";
        if (string.Equals(role, RoleNames.HoD, StringComparison.OrdinalIgnoreCase)) return "HoD";
        return role?.Trim() ?? string.Empty;
    }

    public static string ProgressHeading(ActionTaskUpdate update)
    {
        var body = Normalize(update.Body);
        if (body is "work started." or "work started" or "in progress") return "Work started";
        if (body is "task resumed." or "task resumed" or "work resumed." or "work resumed") return "Work resumed";
        if (body.StartsWith("status changed from ", StringComparison.Ordinal)) return "Status changed";
        if (body is "task marked as blocked." or "task marked as blocked") return "Task blocked";
        if (body is "task submitted for closure." or "task submitted for closure") return "Submitted for closure";
        if (body is "task moved back to assigned status." or "task moved back to assigned status") return "Returned to assigned";
        if (body is "supporting file uploaded." or "supporting file uploaded") return "File added";

        if (string.Equals(update.StatusSnapshot, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)) return "Submitted for closure";
        if (string.Equals(update.StatusSnapshot, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)) return "Task closed";
        if (string.Equals(update.StatusSnapshot, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)) return "Task blocked";

        return "Progress update";
    }

    public static bool ShouldShowProgressBody(ActionTaskUpdate update)
    {
        var body = Normalize(update.Body);
        return body is not "work started."
            and not "work started"
            and not "in progress"
            and not "task resumed."
            and not "task resumed"
            and not "work resumed."
            and not "work resumed"
            and not "task marked as blocked."
            and not "task marked as blocked"
            and not "task submitted for closure."
            and not "task submitted for closure"
            and not "task moved back to assigned status."
            and not "task moved back to assigned status"
            and not "supporting file uploaded."
            and not "supporting file uploaded";
    }

    public static string? ProgressContext(ActionTaskUpdate update)
    {
        var body = Normalize(update.Body);
        if (body == "in progress" && string.Equals(update.StatusSnapshot, ActionTaskStatuses.Assigned, StringComparison.OrdinalIgnoreCase))
        {
            return "Assigned → In Progress";
        }

        if (body is "work started." or "work started") return "Status: In Progress";
        if (body is "task resumed." or "task resumed" or "work resumed." or "work resumed") return "Status: In Progress";
        if (body is "task submitted for closure." or "task submitted for closure") return "Status: Awaiting Closure";
        if (body is "task marked as blocked." or "task marked as blocked") return "Status: Blocked";

        if (!string.IsNullOrWhiteSpace(update.StatusSnapshot))
        {
            return $"Status: {StatusLabel(update.StatusSnapshot)}";
        }

        return null;
    }

    public static string StatusLabel(string? status)
        => string.Equals(status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            ? "Awaiting Closure"
            : status?.Trim() ?? string.Empty;

    public static string AuditActionLabel(string actionType)
        => actionType switch
        {
            "TaskCreated" => "Task created",
            "BacklogItemCreated" => "Backlog item created",
            "StatusUpdated" or "TaskStatusChanged" => "Status changed",
            "Submitted" or "TaskSubmitted" => "Submitted for closure",
            "ReturnedForAction" => "Returned for action",
            "TaskClosedByCommandAuthority" or "TaskClosed" or "Closed" => "Task closed",
            "DueDateChanged" or "TaskDueDateChanged" => "Due date changed",
            "TargetDateChanged" => "Target date changed",
            "TaskAssignedToSprint" or "OutsideSprintTaskAssignedToSprint" => "Added to sprint",
            "TaskRemovedFromSprintKeepAssigned" => "Removed from sprint",
            "TaskMovedToBacklogRemoveAssignee" => "Moved to backlog",
            _ => actionType
        };

    public static string AuditSummary(ActionTaskAuditLog log)
        => log.ActionType switch
        {
            "StatusUpdated" or "TaskStatusChanged" => $"{StatusLabel(log.OldValue)} → {StatusLabel(log.NewValue)}",
            "Submitted" or "TaskSubmitted" => $"{StatusLabel(log.OldValue)} → Awaiting Closure",
            "ReturnedForAction" => "Awaiting Closure → In Progress",
            "TaskClosedByCommandAuthority" or "TaskClosed" or "Closed" => $"{StatusLabel(log.OldValue)} → Closed",
            "DueDateChanged" or "TaskDueDateChanged" or "TargetDateChanged" => $"{FormatDate(log.OldValue)} → {FormatDate(log.NewValue)}",
            _ => log.Remarks?.Trim() ?? string.Empty
        };


    public static bool ShouldShowAuditRemarks(ActionTaskAuditLog log, string? auditSummary)
    {
        var remarks = Normalize(log.Remarks);
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(auditSummary)
            && string.Equals(remarks, Normalize(auditSummary), StringComparison.Ordinal))
        {
            return false;
        }

        if (log.ActionType is "StatusUpdated" or "TaskStatusChanged")
        {
            var newValue = Normalize(log.NewValue);
            var newLabel = Normalize(StatusLabel(log.NewValue));
            if (string.Equals(remarks, newValue, StringComparison.Ordinal)
                || string.Equals(remarks, newLabel, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string FormatDate(string? value)
        => DateTime.TryParse(value, out var parsed)
            ? parsed.ToString("dd MMM yyyy")
            : string.IsNullOrWhiteSpace(value) ? "—" : value;
}
