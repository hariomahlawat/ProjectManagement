using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Outbox;

public enum PrismMediaOutboxEventType
{
    ActivityPhotoUpsert = 0,
    ActivityPhotoRemoved = 1,
    ActivityMetadataRefresh = 2,
    ActivityDeleted = 3
}

public enum PrismMediaOutboxStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    DeadLetter = 3
}

/// <summary>
/// Transactional bridge between PRISM source-domain changes and the separate media catalogue.
/// Rows are created by the ApplicationDbContext SaveChanges interceptor in the same database
/// transaction as the Activity or ActivityAttachment mutation, then processed asynchronously.
/// </summary>
public sealed class PrismMediaOutboxMessage
{
    public long Id { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public PrismMediaOutboxEventType EventType { get; set; }
    public PrismMediaOutboxStatus Status { get; set; } = PrismMediaOutboxStatus.Pending;

    public int? ActivityId { get; set; }
    public int? AttachmentId { get; set; }

    [MaxLength(260)]
    public string? StorageKey { get; set; }

    [MaxLength(512)]
    public string? Reason { get; set; }

    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 10;
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableAfterUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessingStartedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    [MaxLength(128)]
    public string? LockedBy { get; set; }

    public DateTimeOffset? LockExpiresAtUtc { get; set; }

    [MaxLength(2048)]
    public string? LastError { get; set; }
}
