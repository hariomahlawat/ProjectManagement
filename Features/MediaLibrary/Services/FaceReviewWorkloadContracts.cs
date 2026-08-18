namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Scope for operational face-review workload counts. Asset scoping is used when the
/// reviewer entered People review from a selected set of Photos media.
/// </summary>
public sealed record FaceReviewWorkloadQuery(IReadOnlyList<long>? AssetIds = null);

/// <summary>
/// One canonical operational summary for People review. Candidate-search failure is a
/// diagnostic subset of individual review; closed-unidentified appearances are intentionally
/// outside the active unresolved corpus.
/// </summary>
public sealed record FaceReviewWorkloadSummary(
    int KnownMatchCount,
    int IndividualReviewCount,
    int MatchingCount,
    int MatchingFailureCount,
    int ClosedUnidentifiedCount,
    int TotalUnresolvedCount,
    int SuggestedGroupCount,
    int GroupedAppearanceCount,
    int UngroupedAppearanceCount,
    bool GroupingSnapshotAvailable,
    bool GroupingRefreshPending,
    DateTimeOffset? GroupingRefreshedAtUtc,
    string? GroupingFailureReason)
{
    public static FaceReviewWorkloadSummary Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, null, null);
}

public interface IFaceReviewWorkloadService
{
    Task<FaceReviewWorkloadSummary> GetAsync(
        FaceReviewWorkloadQuery query,
        CancellationToken cancellationToken);
}
