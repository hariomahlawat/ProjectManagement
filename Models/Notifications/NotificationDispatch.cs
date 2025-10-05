using System;

namespace ProjectManagement.Models.Notifications;

public sealed class NotificationDispatch
{
    public int Id { get; set; }

    public string RecipientUserId { get; set; } = string.Empty;

    public NotificationKind Kind { get; set; }

    public string? Module { get; set; }

    public string? EventType { get; set; }

    public string? ScopeType { get; set; }

    public string? ScopeId { get; set; }

    public int? ProjectId { get; set; }

    public string? ActorUserId { get; set; }

    public string? Fingerprint { get; set; }

    public string? Route { get; set; }

    public string? Title { get; set; }

    public string? Summary { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? DispatchedUtc { get; set; }

    public string? Error { get; set; }
}
