using System;

namespace ProjectManagement.Contracts.Notifications;

// SECTION: Stable notification payload shared by the bell, Notification Centre and SignalR.
public sealed record NotificationListItem(
    int Id,
    string? Module,
    string? EventType,
    string? ScopeType,
    string? ScopeId,
    int? ProjectId,
    string? ProjectName,
    string? ActorUserId,
    string? Route,
    string? Title,
    string? Summary,
    DateTime CreatedUtc,
    string CreatedDisplayIst,
    DateTime? SeenUtc,
    DateTime? ReadUtc,
    bool IsProjectMuted,
    string? Kind = null,
    string? ActorDisplayName = null,
    string Category = "General",
    string IconCssClass = "bi bi-bell",
    string Priority = "Normal",
    bool IsActionRequired = false,
    DateTime? DeliveredUtc = null);
