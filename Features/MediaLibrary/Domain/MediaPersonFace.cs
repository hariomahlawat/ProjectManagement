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
    public string AssignedByUserId { get; set; } = string.Empty;
    public DateTimeOffset AssignedAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
}
