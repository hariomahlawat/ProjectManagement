using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaAsset
{
    public long Id { get; set; }

    public Guid SourceId { get; set; }
    public MediaLibrarySource Source { get; set; } = null!;

    public MediaAssetOrigin Origin { get; set; }
    public MediaAssetKind Kind { get; set; }

    [Required, MaxLength(1024)]
    public string SourceEntityId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? ParentEntityId { get; set; }

    [MaxLength(2048)]
    public string? RelativePath { get; set; }

    [Required, MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSizeBytes { get; set; }
    public DateTimeOffset? FileModifiedAtUtc { get; set; }

    [MaxLength(128)]
    public string? QuickFingerprint { get; set; }

    [MaxLength(64)]
    public string? ContentHash { get; set; }

    [Required, MaxLength(1024)]
    public string ContextKey { get; set; } = string.Empty;

    [Required, MaxLength(1024)]
    public string CollectionKey { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string ContextTitle { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string ContextSubtitle { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string SourceLabel { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? Caption { get; set; }

    public int? ProjectId { get; set; }
    public DateTimeOffset MediaDateUtc { get; set; }
    public DateTimeOffset IndexedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public Guid LastSeenScanId { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationSeconds { get; set; }

    [MaxLength(128)]
    public string? VersionToken { get; set; }

    public bool IsCover { get; set; }
    public long SortOrder { get; set; }
    public bool IsAvailable { get; set; } = true;
    public MediaAvailabilityStatus AvailabilityStatus { get; set; } = MediaAvailabilityStatus.Available;

    [MaxLength(2048)]
    public string? UnavailableReason { get; set; }

    public DateTimeOffset? UnavailableSinceUtc { get; set; }
    public DateTimeOffset? LastAvailabilityCheckUtc { get; set; }
    public bool IsArchived { get; set; }
    public bool IsDeleted { get; set; }

    public MediaClassification Classification { get; set; }
    public double? ClassificationConfidence { get; set; }
    public bool ClassificationIsManual { get; set; }

    [MaxLength(450)]
    public string? ClassificationUpdatedByUserId { get; set; }

    public DateTimeOffset? ClassifiedAtUtc { get; set; }

    [MaxLength(128)]
    public string? ClassifierVersion { get; set; }
    public MediaProcessingStatus DerivativeStatus { get; set; }
    public MediaProcessingStatus AnalysisStatus { get; set; }

    [MaxLength(128)]
    public string? AnalysisVersion { get; set; }

    public string? AnalysisSignalsJson { get; set; }
    public DateTimeOffset? AnalysedAtUtc { get; set; }

    public MediaProcessingStatus FaceAnalysisStatus { get; set; } = MediaProcessingStatus.NotRequested;

    [MaxLength(256)]
    public string? FaceAnalysisVersion { get; set; }

    public DateTimeOffset? FaceAnalysedAtUtc { get; set; }

    [MaxLength(2048)]
    public string? FaceProcessingFailureReason { get; set; }

    [MaxLength(2048)]
    public string? ProcessingFailureReason { get; set; }

    public int CacheVersion { get; set; } = 1;

    public ICollection<MediaProcessingJob> ProcessingJobs { get; set; } = new List<MediaProcessingJob>();
    public ICollection<MediaFace> Faces { get; set; } = new List<MediaFace>();
}
