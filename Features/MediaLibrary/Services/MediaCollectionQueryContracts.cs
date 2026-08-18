using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaCollectionQuery(
    string? Query,
    string Source,
    string Kind,
    string Classification,
    int? ProjectId,
    int? Year,
    int PageNumber,
    int PageSize,
    bool IncludePeople,
    IReadOnlyList<Guid>? PersonIds = null,
    string PeopleMatch = "all",
    bool IncludeSingletons = false,
    string Sort = "newest");

public sealed record MediaCollectionSummary(
    string CollectionKey,
    MediaAssetOrigin Origin,
    string ContextKey,
    string ContextTitle,
    string ContextSubtitle,
    string SourceLabel,
    int ItemCount,
    int PhotoCount,
    int VideoCount,
    DateTimeOffset FirstMediaDateUtc,
    DateTimeOffset LatestMediaDateUtc,
    long? CoverAssetId,
    int? CoverWidth,
    int? CoverHeight);

public sealed record MediaCollectionQueryResult(
    IReadOnlyList<MediaCollectionSummary> Collections,
    int TotalCollections,
    int TotalItems,
    int TotalPhotos,
    int TotalVideos,
    int PageNumber,
    int PageSize,
    bool HasPreviousPage,
    bool HasNextPage);

public interface IMediaCollectionQueryService
{
    Task<MediaCollectionQueryResult> SearchAsync(
        MediaCollectionQuery query,
        CancellationToken cancellationToken);
}
