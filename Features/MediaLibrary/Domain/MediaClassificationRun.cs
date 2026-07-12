using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaClassificationRun
{
    public long Id { get; set; }
    public long MediaAssetId { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;

    [Required, MaxLength(128)]
    public string ClassifierVersion { get; set; } = string.Empty;

    public MediaClassification PredictedClassification { get; set; }
    public decimal PredictedScore { get; set; }
    public MediaClassification EffectiveClassification { get; set; }
    public MediaClassificationDecisionStatus DecisionStatus { get; set; }

    [MaxLength(128)]
    public string? DecisionReasonCode { get; set; }

    public string CategoryScoresJson { get; set; } = "{}";
    public string SignalsJson { get; set; } = "[]";
    public string MetricsJson { get; set; } = "{}";
    public int ProcessingDurationMilliseconds { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public bool Succeeded { get; set; }

    [MaxLength(2048)]
    public string? FailureReason { get; set; }
}
