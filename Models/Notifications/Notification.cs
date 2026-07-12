using System;

namespace ProjectManagement.Models.Notifications;

public sealed class Notification
{
    public int Id { get; set; }

    public string RecipientUserId { get; set; } = string.Empty;

    public NotificationKind? Kind { get; set; }

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

    /// <summary>
    /// UTC time at which the source business event occurred and was queued.
    /// Retained as CreatedUtc for compatibility with existing data and integrations.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// UTC time at which the outbox item was materialised for the recipient.
    /// </summary>
    public DateTime? DeliveredUtc { get; set; }

    public DateTime? SeenUtc { get; set; }

    public DateTime? ReadUtc { get; set; }

    public int? SourceDispatchId { get; set; }

    public NotificationDispatch? SourceDispatch { get; set; }
}
