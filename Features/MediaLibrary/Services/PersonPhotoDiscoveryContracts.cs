using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Person-centric, read-only view of the existing known-person candidate evidence.
/// It never creates identity evidence and never confirms a person automatically.
/// </summary>
public sealed record PersonPhotoDiscoverySummary(
    Guid PersonId,
    string DisplayName,
    int ConfirmedPhotoCount,
    DateTimeOffset? LatestMediaDateUtc,
    int TrustedReferenceCount,
    int PossibleMatchCount,
    int BackgroundMatchingCount,
    int MatchingFailureCount)
{
    public bool HasTrustedReference => TrustedReferenceCount > 0;
    public bool HasPossibleMatches => PossibleMatchCount > 0;
    public bool MatchingInProgress => BackgroundMatchingCount > 0;
}

public sealed record PersonPhotoCandidate(
    long DecisionId,
    Guid FaceId,
    long AssetId,
    string ContextTitle,
    string ContextSubtitle,
    DateTimeOffset MediaDateUtc,
    double QualityScore,
    double Similarity,
    double BestReferenceSimilarity,
    double MeanTopSimilarity,
    int ReferenceCount,
    double? MarginToNext,
    bool MarginAvailable,
    FaceCandidateConfidenceLevel ConfidenceLevel,
    Guid DecisionConcurrencyToken);

public sealed record PersonPhotoDiscoveryResult(
    PersonPhotoDiscoverySummary Summary,
    IReadOnlyList<PersonPhotoCandidate> Candidates,
    int TotalCandidates);

public interface IPersonPhotoDiscoveryQueryService
{
    Task<PersonPhotoDiscoverySummary?> GetSummaryAsync(
        Guid personId,
        bool includeDiscoveryState,
        CancellationToken cancellationToken);

    Task<PersonPhotoDiscoveryResult?> GetCandidatesAsync(
        Guid personId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revalidates a reviewer-selected set against current pending candidate evidence.
    /// This method is read-only; mutations remain the responsibility of IFaceReviewService.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PersonPhotoCandidate>> GetEligibleCandidatesAsync(
        Guid personId,
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken);
}

internal sealed class PersonPhotoDiscoveryDatabaseRow
{
    public long DecisionId { get; init; }
    public Guid FaceId { get; init; }
    public long AssetId { get; init; }
    public string ContextTitle { get; init; } = string.Empty;
    public string ContextSubtitle { get; init; } = string.Empty;
    public DateTimeOffset MediaDateUtc { get; init; }
    public double QualityScore { get; init; }
    public double? Similarity { get; init; }
    public double? BestReferenceSimilarity { get; init; }
    public double? MeanTopSimilarity { get; init; }
    public int ReferenceCount { get; init; }
    public double? MarginToNext { get; init; }
    public bool MarginAvailable { get; init; }
    public FaceCandidateConfidenceLevel ConfidenceLevel { get; init; }
    public Guid DecisionConcurrencyToken { get; init; }
}
