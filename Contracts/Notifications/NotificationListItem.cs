using System;

namespace ProjectManagement.Contracts.Notifications;

// SECTION: Notification List Payload
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
    bool IsProjectMuted);
