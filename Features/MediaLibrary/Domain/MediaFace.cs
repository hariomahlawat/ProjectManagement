using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaFace
{
    public Guid Id { get; set; }
    public long MediaAssetId { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? LandmarksJson { get; set; }
    public double DetectionConfidence { get; set; }
    public double QualityScore { get; set; }
    public FaceQualityStatus QualityStatus { get; set; }
    public double? BlurScore { get; set; }
    public double? BrightnessScore { get; set; }
    public double? PoseScore { get; set; }
    public bool IsSuppressed { get; set; }
    public DateTimeOffset? SuppressedAtUtc { get; set; }
    [MaxLength(450)] public string? SuppressedByUserId { get; set; }
    [MaxLength(128)] public string DetectorModelKey { get; set; } = string.Empty;
    [MaxLength(128)] public string DetectorModelVersion { get; set; } = string.Empty;
    [MaxLength(1024)] public string? ReviewThumbnailPath { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<MediaFaceEmbedding> Embeddings { get; set; } = new List<MediaFaceEmbedding>();
    public ICollection<MediaPersonFace> PersonAssignments { get; set; } = new List<MediaPersonFace>();
}
