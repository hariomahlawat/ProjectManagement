using System;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

/// <summary>
/// Central presentation catalogue for notification categories, iconography and urgency.
/// Producers remain responsible for event-specific title and summary text; this class keeps
/// the visual and behavioural treatment consistent across every module.
/// </summary>
public static class NotificationPresentationCatalog
{
    public static NotificationPresentation Resolve(
        NotificationKind? kind,
        string? module,
        string? eventType)
    {
        if (kind.HasValue)
        {
            return kind.Value switch
            {
                NotificationKind.MentionedInRemark => new("Collaboration", "bi bi-at", "High", true),
                NotificationKind.RemarkCreated => new("Collaboration", "bi bi-chat-left-text", "Normal", false),

                NotificationKind.PlanSubmitted => new("Approvals", "bi bi-send-check", "High", true),
                NotificationKind.PlanApproved => new("Approvals", "bi bi-check2-circle", "Normal", false),
                NotificationKind.PlanRejected => new("Approvals", "bi bi-x-circle", "High", true),

                NotificationKind.StageStatusChanged => new("Project lifecycle", "bi bi-diagram-3", "Normal", false),
                NotificationKind.StageAssigned => new("Project lifecycle", "bi bi-person-check", "High", true),

                NotificationKind.DocumentPublished => new("Documents", "bi bi-file-earmark-check", "Normal", false),
                NotificationKind.DocumentReplaced => new("Documents", "bi bi-file-earmark-arrow-up", "Normal", false),
                NotificationKind.DocumentArchived => new("Documents", "bi bi-archive", "Normal", false),
                NotificationKind.DocumentRestored => new("Documents", "bi bi-arrow-counterclockwise", "Normal", false),
                NotificationKind.DocumentDeleted => new("Documents", "bi bi-trash3", "High", false),

                NotificationKind.RoleAssignmentsChanged => new("Administration", "bi bi-people", "High", true),
                NotificationKind.ProjectAssignmentChanged => new("Assignments", "bi bi-person-workspace", "High", true),

                NotificationKind.TrainingDeleteRequested => new("Approvals", "bi bi-mortarboard", "High", true),
                NotificationKind.TrainingDeleteApproved => new("Approvals", "bi bi-check2-circle", "Normal", false),
                NotificationKind.TrainingDeleteRejected => new("Approvals", "bi bi-x-circle", "High", true),
                NotificationKind.ActivityDeleteRequested => new("Approvals", "bi bi-calendar2-x", "High", true),
                NotificationKind.ActivityDeleteApproved => new("Approvals", "bi bi-check2-circle", "Normal", false),
                NotificationKind.ActivityDeleteRejected => new("Approvals", "bi bi-x-circle", "High", true),

                NotificationKind.ActionTaskAssigned => new("Action tracker", "bi bi-person-check", "High", true),
                NotificationKind.ActionTaskProgressUpdated => new("Action tracker", "bi bi-activity", "Normal", false),
                NotificationKind.ActionTaskStatusChanged => new("Action tracker", "bi bi-arrow-repeat", "Normal", false),
                NotificationKind.ActionTaskBlocked => new("Action tracker", "bi bi-exclamation-octagon", "Urgent", true),
                NotificationKind.ActionTaskSubmittedForClosure => new("Action tracker", "bi bi-send-check", "High", true),
                NotificationKind.ActionTaskClosed => new("Action tracker", "bi bi-check2-circle", "Normal", false),
                NotificationKind.ActionTaskDueDateChanged => new("Action tracker", "bi bi-calendar-event", "High", true),
                NotificationKind.ActionTaskMovedToBacklog => new("Action tracker", "bi bi-inbox", "Normal", false),
                NotificationKind.ActionTaskRemovedFromSprint => new("Action tracker", "bi bi-dash-circle", "Normal", false),
                NotificationKind.ActionTaskAddedToSprint => new("Action tracker", "bi bi-plus-circle", "Normal", false),

                _ => Default(module, eventType),
            };
        }

        return Default(module, eventType);
    }

    private static NotificationPresentation Default(string? module, string? eventType)
    {
        if (Contains(module, "document") || Contains(eventType, "document"))
        {
            return new("Documents", "bi bi-file-earmark-text", "Normal", false);
        }

        if (Contains(module, "stage") || Contains(eventType, "stage"))
        {
            return new("Project lifecycle", "bi bi-diagram-3", "Normal", false);
        }

        if (Contains(module, "approval") || Contains(eventType, "approved") || Contains(eventType, "rejected"))
        {
            return new("Approvals", "bi bi-clipboard-check", "High", true);
        }

        if (Contains(module, "action") || Contains(eventType, "task"))
        {
            return new("Action tracker", "bi bi-list-check", "Normal", false);
        }

        return new("General", "bi bi-bell", "Normal", false);
    }

    private static bool Contains(string? value, string fragment)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}

public sealed record NotificationPresentation(
    string Category,
    string IconCssClass,
    string Priority,
    bool IsActionRequired);
