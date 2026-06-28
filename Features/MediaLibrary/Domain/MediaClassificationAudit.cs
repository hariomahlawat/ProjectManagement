using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaClassificationAudit
{
    public long Id { get; set; }
    public long MediaAssetId { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;
    public MediaClassification PreviousClassification { get; set; }
    public MediaClassification NewClassification { get; set; }
    public bool PreviousWasManual { get; set; }
    public bool NewIsManual { get; set; }
    [Required, MaxLength(450)] public string ChangedByUserId { get; set; } = string.Empty;
    [MaxLength(1024)] public string? Reason { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
}
