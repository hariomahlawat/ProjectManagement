using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProjectManagement.Configuration;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

/// <summary>
/// Human-facing labels for the task timeline and audit trail. Keeps raw model
/// terminology and audit storage details out of the UI without changing persisted history.
/// </summary>
public static class ActionTaskPresentation
{
    private static readonly Regex UpdateMarkerRegex = new(@"\s*\[update:(?<id>\d+)\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        if (body.StartsWith("responsibility reassigned.", StringComparison.Ordinal)) return "Task reassigned";
        if (body.StartsWith("priority changed from ", StringComparison.Ordinal)) return "Priority changed";

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

        var isNamedWorkflowEvent = body is "work started." or "work started"
            or "task resumed." or "task resumed"
            or "work resumed." or "work resumed"
            or "task submitted for closure." or "task submitted for closure"
            or "task marked as blocked." or "task marked as blocked";

        if (isNamedWorkflowEvent
            || body.StartsWith("responsibility reassigned.", StringComparison.Ordinal)
            || body.StartsWith("priority changed from ", StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(update.StatusSnapshot, ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(update.StatusSnapshot, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(update.StatusSnapshot, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

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
            "TaskDetailsUpdated" => "Task details updated",
            "TaskReassigned" => "Task reassigned",
            "PriorityChanged" => "Priority changed",
            "RemarkEdited" => "Remark edited",
            "RemarkDeleted" => "Remark deleted",
            _ => actionType
        };

    public static string AuditSummary(ActionTaskAuditLog log)
        => AuditSummary(log, null);

    public static string AuditSummary(ActionTaskAuditLog log, Func<string?, string>? resolveUserName)
        => log.ActionType switch
        {
            "StatusUpdated" or "TaskStatusChanged" => $"{StatusLabel(log.OldValue)} → {StatusLabel(log.NewValue)}",
            "Submitted" or "TaskSubmitted" => $"{StatusLabel(log.OldValue)} → Awaiting Closure",
            "ReturnedForAction" => "Awaiting Closure → In Progress",
            "TaskClosedByCommandAuthority" or "TaskClosed" or "Closed" => $"{StatusLabel(log.OldValue)} → Closed",
            "DueDateChanged" or "TaskDueDateChanged" or "TargetDateChanged" => $"{FormatDate(log.OldValue)} → {FormatDate(log.NewValue)}",
            "TaskDetailsUpdated" => BuildTaskDetailsAuditSummary(log),
            "TaskReassigned" => BuildReassignmentSummary(log, resolveUserName),
            "PriorityChanged" => $"{log.OldValue} → {log.NewValue}",
            "RemarkEdited" => "Human remark corrected",
            "RemarkDeleted" => "Human remark removed",
            _ => AuditDisplayRemarks(log) ?? string.Empty
        };

    public static IReadOnlyList<ActionTaskAuditFieldChange> AuditFieldChanges(ActionTaskAuditLog log)
    {
        if (!string.Equals(log.ActionType, "TaskDetailsUpdated", StringComparison.Ordinal))
        {
            return Array.Empty<ActionTaskAuditFieldChange>();
        }

        if (!TryParseTaskDetailsSnapshot(log.OldValue, out var oldSnapshot)
            || !TryParseTaskDetailsSnapshot(log.NewValue, out var newSnapshot))
        {
            return Array.Empty<ActionTaskAuditFieldChange>();
        }

        var result = new List<ActionTaskAuditFieldChange>(2);
        if (!string.Equals(oldSnapshot.Title, newSnapshot.Title, StringComparison.Ordinal))
        {
            result.Add(new ActionTaskAuditFieldChange("Title", oldSnapshot.Title, newSnapshot.Title));
        }
        if (!string.Equals(oldSnapshot.Description, newSnapshot.Description, StringComparison.Ordinal))
        {
            result.Add(new ActionTaskAuditFieldChange("Task brief", oldSnapshot.Description, newSnapshot.Description));
        }
        return result;
    }

    public static bool ShouldShowAuditRemarks(ActionTaskAuditLog log, string? auditSummary)
    {
        var displayRemarks = AuditDisplayRemarks(log);
        var remarks = Normalize(displayRemarks);
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return false;
        }

        if (string.Equals(log.ActionType, "TaskDetailsUpdated", StringComparison.Ordinal))
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

    public static string? AuditDisplayRemarks(ActionTaskAuditLog log)
    {
        if (string.IsNullOrWhiteSpace(log.Remarks))
        {
            return null;
        }
        return UpdateMarkerRegex.Replace(log.Remarks.Trim(), string.Empty).Trim();
    }

    // SECTION: Edited state is derived from immutable audit history, avoiding a schema migration.
    public static IReadOnlySet<int> ResolveEditedUpdateIds(
        IReadOnlyList<ActionTaskAuditLog> logs,
        IReadOnlyList<ActionTaskUpdate> updates)
    {
        var result = new HashSet<int>();
        var updateById = updates.ToDictionary(update => update.Id);

        foreach (var log in logs.Where(log => string.Equals(log.ActionType, "RemarkEdited", StringComparison.Ordinal)))
        {
            if (TryExtractUpdateId(log.Remarks, out var updateId) && updateById.ContainsKey(updateId))
            {
                result.Add(updateId);
                continue;
            }

            // Backward-compatible fallback for edits written before the update-id audit marker.
            if (string.IsNullOrWhiteSpace(log.NewValue))
            {
                continue;
            }

            var candidates = updates
                .Where(update => !update.IsDeleted
                    && update.CreatedAtUtc <= log.PerformedAt
                    && string.Equals(update.Body, log.NewValue, StringComparison.Ordinal))
                .Select(update => update.Id)
                .Distinct()
                .ToList();
            if (candidates.Count == 1)
            {
                result.Add(candidates[0]);
            }
        }

        return result;
    }

    private static bool TryExtractUpdateId(string? remarks, out int updateId)
    {
        updateId = 0;
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return false;
        }
        var match = UpdateMarkerRegex.Match(remarks);
        return match.Success && int.TryParse(match.Groups["id"].Value, out updateId);
    }

    private static string BuildReassignmentSummary(ActionTaskAuditLog log, Func<string?, string>? resolveUserName)
    {
        if (resolveUserName is null)
        {
            return "Responsible person changed";
        }
        var oldName = resolveUserName(log.OldValue);
        var newName = resolveUserName(log.NewValue);
        return $"{oldName} → {newName}";
    }

    private static string BuildTaskDetailsAuditSummary(ActionTaskAuditLog log)
    {
        if (TryParseTaskDetailsSnapshot(log.OldValue, out var oldSnapshot)
            && TryParseTaskDetailsSnapshot(log.NewValue, out var newSnapshot))
        {
            var titleChanged = !string.Equals(oldSnapshot.Title, newSnapshot.Title, StringComparison.Ordinal);
            var briefChanged = !string.Equals(oldSnapshot.Description, newSnapshot.Description, StringComparison.Ordinal);
            if (titleChanged && briefChanged) return "Title and task brief changed";
            if (titleChanged) return $"Title: {oldSnapshot.Title} → {newSnapshot.Title}";
            if (briefChanged) return "Task brief changed";
            return "Task details updated";
        }

        // Backward compatibility for V2.3 audit rows that stored the title directly.
        return string.Equals(log.OldValue, log.NewValue, StringComparison.Ordinal)
            ? "Task brief updated"
            : $"{log.OldValue} → {log.NewValue}";
    }

    private static bool TryParseTaskDetailsSnapshot(string? value, out TaskDetailsAuditSnapshot snapshot)
    {
        snapshot = new TaskDetailsAuditSnapshot(string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(value) || !value.TrimStart().StartsWith('{'))
        {
            return false;
        }
        try
        {
            snapshot = JsonSerializer.Deserialize<TaskDetailsAuditSnapshot>(value) ?? snapshot;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string FormatDate(string? value)
        => DateTime.TryParse(value, out var parsed)
            ? parsed.ToString("dd MMM yyyy")
            : string.IsNullOrWhiteSpace(value) ? "—" : value;

    private sealed record TaskDetailsAuditSnapshot(string Title, string Description);
}

public sealed record ActionTaskAuditFieldChange(string Label, string OldValue, string NewValue);
