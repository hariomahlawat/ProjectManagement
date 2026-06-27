using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectManagement.Features.MediaLibrary.Domain;

/// <summary>
/// Reserved domain shape for the future People module. It is intentionally not mapped by
/// the catalogue DbContext until People is explicitly enabled and its separate migration
/// is deployed.
/// </summary>
[NotMapped]
public sealed class MediaFace
{
    public Guid Id { get; set; }
    public long MediaAssetId { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double DetectionConfidence { get; set; }
    public double QualityScore { get; set; }
    public float[]? Embedding { get; set; }
    public Guid? PersonId { get; set; }
    public Guid? FaceClusterId { get; set; }
    public FaceIdentityStatus IdentityStatus { get; set; }
    public double? MatchConfidence { get; set; }
    public bool IsManuallyConfirmed { get; set; }

    [MaxLength(128)]
    public string DetectorModelVersion { get; set; } = string.Empty;

    [MaxLength(128)]
    public string EmbeddingModelVersion { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
