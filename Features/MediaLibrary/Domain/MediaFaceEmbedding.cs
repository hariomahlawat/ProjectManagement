using System.ComponentModel.DataAnnotations;
namespace ProjectManagement.Features.MediaLibrary.Domain;
public sealed class MediaFaceEmbedding
{
    public long Id { get; set; }
    public Guid MediaFaceId { get; set; }
    public MediaFace MediaFace { get; set; } = null!;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public int Dimension { get; set; }
    [MaxLength(128)] public string ModelKey { get; set; } = string.Empty;
    [MaxLength(128)] public string ModelVersion { get; set; } = string.Empty;
    [MaxLength(32)] public string Normalization { get; set; } = "L2";
    public double QualityScore { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? InvalidatedAtUtc { get; set; }
}
