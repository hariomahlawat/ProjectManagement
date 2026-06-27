using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaLibrarySource
{
    public Guid Id { get; set; }

    [Required, MaxLength(64)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    public MediaLibrarySourceType SourceType { get; set; }

    [MaxLength(2048)]
    public string? RootPath { get; set; }

    public bool IsEnabled { get; set; }
    public bool IsVisibleInLibrary { get; set; } = true;
    public bool IsReadOnly { get; set; } = true;
    public bool IncludeSubfolders { get; set; } = true;
    public bool IsConfigurationManaged { get; set; }
    public bool IsDeleted { get; set; }
    public int ScanIntervalMinutes { get; set; } = 30;

    public string AllowedExtensionsJson { get; set; } = "[]";

    [MaxLength(128)]
    public string? ConfigurationFingerprint { get; set; }

    public DateTimeOffset? LastScanStartedAtUtc { get; set; }
    public DateTimeOffset? LastScanCompletedAtUtc { get; set; }
    public DateTimeOffset? LastSuccessfulScanAtUtc { get; set; }
    public DateTimeOffset? ScanRequestedAtUtc { get; set; }

    [MaxLength(128)]
    public string? ScanLockedBy { get; set; }

    public DateTimeOffset? ScanLockExpiresAtUtc { get; set; }

    [MaxLength(64)]
    public string ScanStatus { get; set; } = "Never";

    [MaxLength(2048)]
    public string? LastError { get; set; }

    [MaxLength(64)]
    public string HealthStatus { get; set; } = "Unknown";

    [MaxLength(2048)]
    public string? HealthMessage { get; set; }

    public DateTimeOffset? LastHealthCheckedAtUtc { get; set; }
    public DateTimeOffset? DisconnectedAtUtc { get; set; }

    public long IndexedAssetCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<MediaAsset> Assets { get; set; } = new List<MediaAsset>();
}
