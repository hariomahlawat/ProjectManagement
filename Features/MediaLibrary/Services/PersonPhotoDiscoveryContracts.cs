using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public enum PersonPhotoDiscoveryEvidenceSource
{
    DirectPersonCandidate = 0,
    IdentityGroupCandidate = 1
}

public enum PersonPhotoDiscoveryBand
{
    Strong = 0,
    Moderate = 1,
    Other = 2
}

/// <summary>
/// Person-centric summary of current human-confirmed photographs and machine-generated
/// review evidence. Counts are suggestions only; no identity is inferred automatically.
/// </summary>
public sealed record PersonPhotoDiscoverySummary(
    Guid PersonId,
    string DisplayName,
    int ConfirmedPhotoCount,
    DateTimeOffset? LatestMediaDateUtc,
    int TrustedReferenceCount,
    int PossibleMatchCount,
    int BackgroundMatchingCount,
    int MatchingFailureCount,
    int DirectCandidateCount = 0,
    int GroupCandidateAppearanceCount = 0,
    int GroupCandidateCount = 0)
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
    Guid DecisionConcurrencyToken,
    PersonPhotoDiscoveryEvidenceSource EvidenceSource = PersonPhotoDiscoveryEvidenceSource.DirectPersonCandidate,
    PersonPhotoDiscoveryBand Band = PersonPhotoDiscoveryBand.Moderate,
    string? GroupKey = null,
    double? GroupPersonSimilarity = null,
    double? SimilarityToGroupRepresentative = null);

public sealed record PersonPhotoIdentityGroupCandidate(
    string GroupKey,
    double PersonSimilarity,
    double CohesionScore,
    int PhotoCount,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    IReadOnlyList<PersonPhotoCandidate> Candidates);

public sealed record PersonPhotoDiscoveryResult(
    PersonPhotoDiscoverySummary Summary,
    IReadOnlyList<PersonPhotoCandidate> StrongCandidates,
    IReadOnlyList<PersonPhotoCandidate> ModerateCandidates,
    IReadOnlyList<PersonPhotoCandidate> OtherCandidates,
    IReadOnlyList<PersonPhotoIdentityGroupCandidate> IdentityGroups,
    int TotalCandidates,
    int TotalDirectCandidates,
    int TotalGroupCandidateAppearances)
{
    public IReadOnlyList<PersonPhotoCandidate> Candidates
        => StrongCandidates.Concat(ModerateCandidates).Concat(OtherCandidates).ToArray();
}

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
    /// Revalidates a reviewer-selected set against current direct or identity-group evidence.
    /// Mutations remain the responsibility of IFaceReviewService.
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
