using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Features.MediaLibrary.Domain;

public sealed class MediaFaceReviewDecision
{
    public long Id { get; set; }
    public Guid MediaFaceId { get; set; }
    public MediaFace MediaFace { get; set; } = null!;
    public Guid? CandidatePersonId { get; set; }
    public MediaPerson? CandidatePerson { get; set; }
    public FaceReviewDecisionType Decision { get; set; }
    public double? Similarity { get; set; }
    public double? BestReferenceSimilarity { get; set; }
    public double? MeanTopSimilarity { get; set; }
    public int ReferenceCount { get; set; }
    public double? MarginToNext { get; set; }
    public bool MarginAvailable { get; set; }
    public FaceCandidateConfidenceLevel ConfidenceLevel { get; set; } = FaceCandidateConfidenceLevel.None;

    [MaxLength(128)]
    public string ModelKey { get; set; } = string.Empty;

    [MaxLength(128)]
    public string ModelVersion { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? DecidedByUserId { get; set; }

    [MaxLength(1024)]
    public string? Notes { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
}
