using System;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.ViewModels.Notifications;

public sealed record NotificationDisplayModel
{
    public int Id { get; init; }
    public string? Kind { get; init; }
    public string? Module { get; init; }
    public string? EventType { get; init; }
    public string? ScopeType { get; init; }
    public string? ScopeId { get; init; }
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? ActorUserId { get; init; }
    public string? ActorDisplayName { get; init; }
    public string? Route { get; init; }
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public string? SummaryTooltip { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? DeliveredUtc { get; init; }
    public string CreatedDisplayIst { get; init; } = string.Empty;
    public DateTime? SeenUtc { get; init; }
    public DateTime? ReadUtc { get; init; }
    public bool IsProjectMuted { get; init; }
    public string Category { get; init; } = "General";
    public string IconCssClass { get; init; } = "bi bi-bell";
    public string Priority { get; init; } = "Normal";
    public bool IsActionRequired { get; init; }
    public bool IsRead => ReadUtc.HasValue;
    public bool IsSeen => SeenUtc.HasValue;

    public static NotificationDisplayModel FromContract(NotificationListItem notification)
        => new()
        {
            Id = notification.Id,
            Kind = notification.Kind,
            Module = notification.Module,
            EventType = notification.EventType,
            ScopeType = notification.ScopeType,
            ScopeId = notification.ScopeId,
            ProjectId = notification.ProjectId,
            ProjectName = notification.ProjectName,
            ActorUserId = notification.ActorUserId,
            ActorDisplayName = notification.ActorDisplayName,
            Route = notification.Route,
            Title = notification.Title,
            Summary = notification.Summary,
            SummaryTooltip = notification.SummaryTooltip,
            CreatedUtc = notification.CreatedUtc,
            DeliveredUtc = notification.DeliveredUtc,
            CreatedDisplayIst = string.IsNullOrWhiteSpace(notification.CreatedDisplayIst)
                ? TimeFmt.ToIst(notification.CreatedUtc)
                : notification.CreatedDisplayIst,
            SeenUtc = notification.SeenUtc,
            ReadUtc = notification.ReadUtc,
            IsProjectMuted = notification.IsProjectMuted,
            Category = notification.Category,
            IconCssClass = notification.IconCssClass,
            Priority = notification.Priority,
            IsActionRequired = notification.IsActionRequired,
        };
}
