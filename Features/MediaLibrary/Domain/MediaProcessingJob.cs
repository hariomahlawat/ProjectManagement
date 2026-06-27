using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaProcessingJob
{
    public long Id { get; set; }
    public long MediaAssetId { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;

    public MediaProcessingJobType JobType { get; set; }
    public MediaProcessingJobStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;

    public DateTimeOffset AvailableAfterUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    [MaxLength(128)]
    public string? LockedBy { get; set; }

    public DateTimeOffset? LockExpiresAtUtc { get; set; }

    [MaxLength(128)]
    public string? FailureCode { get; set; }

    [MaxLength(2048)]
    public string? FailureMessage { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
