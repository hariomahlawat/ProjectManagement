using System;
using ProjectManagement.Contracts.Notifications;

namespace ProjectManagement.ViewModels.Notifications;

public sealed record NotificationDisplayModel
{
    public int Id { get; init; }

    public string? Module { get; init; }

    public string? EventType { get; init; }

    public string? ScopeType { get; init; }

    public string? ScopeId { get; init; }

    public int? ProjectId { get; init; }

    public string? ProjectName { get; init; }

    public string? ActorUserId { get; init; }

    public string? Route { get; init; }

    public string? Title { get; init; }

    public string? Summary { get; init; }

    public DateTime CreatedUtc { get; init; }

    public DateTime? SeenUtc { get; init; }

    public DateTime? ReadUtc { get; init; }

    public bool IsProjectMuted { get; init; }

    public bool IsRead => ReadUtc.HasValue;

    public static NotificationDisplayModel FromContract(NotificationListItem notification)
        => new()
        {
            Id = notification.Id,
            Module = notification.Module,
            EventType = notification.EventType,
            ScopeType = notification.ScopeType,
            ScopeId = notification.ScopeId,
            ProjectId = notification.ProjectId,
            ProjectName = notification.ProjectName,
            ActorUserId = notification.ActorUserId,
            Route = notification.Route,
            Title = notification.Title,
            Summary = notification.Summary,
            CreatedUtc = notification.CreatedUtc,
            SeenUtc = notification.SeenUtc,
            ReadUtc = notification.ReadUtc,
            IsProjectMuted = notification.IsProjectMuted,
        };
}
