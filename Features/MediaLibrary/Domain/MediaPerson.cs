using System.ComponentModel.DataAnnotations;
namespace ProjectManagement.Features.MediaLibrary.Domain;
public sealed class MediaPerson
{
    public Guid Id { get; set; }
    [Required, MaxLength(200)] public string DisplayName { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string NormalizedName { get; set; } = string.Empty;
    public MediaPersonStatus Status { get; set; } = MediaPersonStatus.Unreviewed;
    public Guid? RepresentativeFaceId { get; set; }
    public bool IsHidden { get; set; }
    public bool IsMinor { get; set; }
    [Required, MaxLength(450)] public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<MediaPersonFace> FaceAssignments { get; set; } = new List<MediaPersonFace>();
}
