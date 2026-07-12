namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaFaceCluster
{
    public Guid Id { get; set; }
    public Guid? PersonId { get; set; }
    public MediaPerson? Person { get; set; }
    public Guid? RepresentativeFaceId { get; set; }
    public int FaceCount { get; set; }
    public FaceClusterStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<MediaFace> Faces { get; set; } = new List<MediaFace>();
}
