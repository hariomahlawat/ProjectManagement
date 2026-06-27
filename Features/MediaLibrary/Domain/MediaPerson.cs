using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaPerson
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string NormalizedName { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? LinkedUserId { get; set; }

    [MaxLength(200)]
    public string? Designation { get; set; }

    [MaxLength(200)]
    public string? Organisation { get; set; }

    public Guid? RepresentativeFaceId { get; set; }
    public bool IsHidden { get; set; }
    public bool IsMinor { get; set; }

    [Required, MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<MediaFace> Faces { get; set; } = new List<MediaFace>();
    public ICollection<MediaFaceCluster> Clusters { get; set; } = new List<MediaFaceCluster>();
}
