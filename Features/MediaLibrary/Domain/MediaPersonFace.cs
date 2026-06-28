using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaPersonFace
{
    public long Id { get; set; }
    public Guid MediaPersonId { get; set; }
    public MediaPerson MediaPerson { get; set; } = null!;
    public Guid MediaFaceId { get; set; }
    public MediaFace MediaFace { get; set; } = null!;
    public FaceAssignmentType AssignmentType { get; set; }
    public double? AssignmentConfidence { get; set; }

    [Required, MaxLength(450)]
    public string AssignedByUserId { get; set; } = string.Empty;

    public DateTimeOffset AssignedAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }

    [MaxLength(450)]
    public string? RemovedByUserId { get; set; }

    [MaxLength(1024)]
    public string? RemovalReason { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
