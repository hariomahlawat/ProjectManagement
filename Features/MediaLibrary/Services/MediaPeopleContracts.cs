namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaPeopleIndexQuery(
    string? Query,
    string Sort,
    bool IncludeHidden,
    int PageNumber,
    int PageSize);

public sealed record MediaPersonCard(
    Guid Id,
    string DisplayName,
    Guid? RepresentativeFaceId,
    int ConfirmedFaceCount,
    int PhotoCount,
    DateTimeOffset? LatestMediaDateUtc,
    bool IsHidden,
    bool IsMinor,
    Guid ConcurrencyToken);

public sealed record MediaPeopleIndexResult(
    IReadOnlyList<MediaPersonCard> People,
    int TotalPeople,
    int PendingReviewCount,
    int UnidentifiedFaceCount,
    int PageNumber,
    int PageSize,
    bool HasPreviousPage,
    bool HasNextPage,
    int KnownPersonSuggestionCount = 0,
    int CandidateSearchPendingCount = 0,
    int CandidateSearchFailureCount = 0);

public sealed record MediaPersonDetailsResult(
    Guid Id,
    string DisplayName,
    Guid? RepresentativeFaceId,
    bool IsHidden,
    bool IsMinor,
    Guid ConcurrencyToken,
    int ConfirmedFaceCount,
    int PhotoCount,
    DateTimeOffset? FirstMediaDateUtc,
    DateTimeOffset? LatestMediaDateUtc,
    IReadOnlyList<MediaPersonPhotoItem> Photos,
    IReadOnlyList<MediaPersonOption> MergeTargets,
    IReadOnlyList<MediaIdentityHistoryItem> IdentityHistory);

public sealed record MediaPersonPhotoItem(
    long AssetId,
    Guid FaceId,
    string ContextTitle,
    string ContextSubtitle,
    string SourceLabel,
    DateTimeOffset MediaDateUtc,
    int? Width,
    int? Height,
    double FaceQualityScore,
    bool IsRepresentative);

public sealed record MediaPersonOption(Guid Id, string DisplayName);

public sealed record MediaIdentityHistoryItem(
    long Id,
    string Action,
    string ActionLabel,
    string? Notes,
    string PerformedByUserId,
    DateTimeOffset PerformedAtUtc,
    Guid? FaceId,
    Guid? PreviousPersonId,
    Guid? NewPersonId);

public enum FaceReviewQueueKind
{
    KnownMatches = 0,
    Unidentified = 1
}

public sealed record FaceReviewCandidateItem(
    long DecisionId,
    Guid PersonId,
    string DisplayName,
    Guid? RepresentativeFaceId,
    double? Similarity,
    Guid ConcurrencyToken,
    int Rank = 0,
    double? MarginToNext = null,
    bool IsStrong = false,
    bool IsAmbiguous = false);

public sealed record FaceReviewQueueItem(
    Guid FaceId,
    long AssetId,
    string ContextTitle,
    string ContextSubtitle,
    DateTimeOffset MediaDateUtc,
    double QualityScore,
    IReadOnlyList<FaceReviewCandidateItem> Candidates,
    FaceCandidateSearchStatus CandidateSearchStatus = FaceCandidateSearchStatus.NotRequested,
    string? CandidateSearchFailureReason = null);

public sealed record FaceReviewQueueResult(
    IReadOnlyList<FaceReviewQueueItem> Items,
    IReadOnlyList<MediaPersonOption> AvailablePeople,
    int TotalFaces,
    int PageNumber,
    int PageSize,
    bool HasPreviousPage,
    bool HasNextPage,
    int KnownMatchCount = 0,
    int UnidentifiedCount = 0,
    int CandidateSearchPendingCount = 0,
    int CandidateSearchFailureCount = 0);

public interface IMediaPeopleQueryService
{
    Task<MediaPeopleIndexResult> GetIndexAsync(
        MediaPeopleIndexQuery query,
        CancellationToken cancellationToken);

    Task<MediaPersonDetailsResult?> GetPersonAsync(
        Guid personId,
        CancellationToken cancellationToken);

    Task<FaceReviewQueueResult> GetReviewQueueAsync(
        FaceReviewQueueKind kind,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaPersonOption>> GetPersonOptionsAsync(
        CancellationToken cancellationToken);
}
