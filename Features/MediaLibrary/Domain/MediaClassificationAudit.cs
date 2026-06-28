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
    public MediaClassification AutomaticPredictedClassification { get; set; }
    public decimal AutomaticPredictedScore { get; set; }
    public MediaClassificationDecisionStatus PreviousDecisionStatus { get; set; }
    public MediaClassificationDecisionStatus NewDecisionStatus { get; set; }
    [MaxLength(128)] public string? CorrelationId { get; set; }
    [Required, MaxLength(450)] public string ChangedByUserId { get; set; } = string.Empty;
    [MaxLength(1024)] public string? Reason { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
}
