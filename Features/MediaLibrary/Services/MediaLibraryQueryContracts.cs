using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaLibraryQuery(
    string? Query,
    string Source,
    string Kind,
    string Classification,
    int? ProjectId,
    int? Year,
    int PageNumber,
    int PageSize);

public sealed record MediaLibraryQueryItem(
    long Id,
    Guid SourceId,
    MediaAssetOrigin Origin,
    MediaAssetKind Kind,
    string SourceEntityId,
    string? ParentEntityId,
    string ContextKey,
    string CollectionKey,
    string ContextTitle,
    string ContextSubtitle,
    string SourceLabel,
    string Title,
    string? Caption,
    string OriginalFileName,
    DateTimeOffset MediaDateUtc,
    int? Width,
    int? Height,
    int? DurationSeconds,
    bool IsCover,
    long SortOrder,
    int CacheVersion,
    string? VersionToken);

public sealed record MediaLibraryProjectOption(int Id, string Name);

public sealed record MediaLibraryStatistics(int Total, int Photos, int Videos, int Collections);

public enum MediaLibraryQueryOperation
{
    CatalogueDisabled,
    PrimaryTimeline,
    Statistics,
    Years,
    Projects,
    PrismSourceStatus
}

public sealed record MediaLibraryQueryWarning(
    MediaLibraryQueryOperation Operation,
    string Reference,
    string Message,
    DateTimeOffset OccurredAtUtc);

public sealed record MediaLibraryQueryResult(
    IReadOnlyList<MediaLibraryQueryItem> Items,
    IReadOnlyList<MediaLibraryProjectOption> Projects,
    IReadOnlyList<int> Years,
    MediaLibraryStatistics Statistics,
    int PageNumber,
    int PageSize,
    bool HasPreviousPage,
    bool HasNextPage,
    bool IsAvailable,
    bool IsPrimaryQuerySuccessful,
    bool HasPrismCatalogue,
    IReadOnlyList<MediaLibraryQueryWarning> Warnings,
    string? Warning)
{
    public bool IsDegraded => IsAvailable && Warnings.Count > 0;

    public static MediaLibraryQueryResult Unavailable(
        int pageNumber,
        int pageSize,
        string warning,
        MediaLibraryQueryWarning? diagnostic = null)
        => new(
            Array.Empty<MediaLibraryQueryItem>(),
            Array.Empty<MediaLibraryProjectOption>(),
            Array.Empty<int>(),
            new MediaLibraryStatistics(0, 0, 0, 0),
            Math.Max(1, pageNumber),
            Math.Max(1, pageSize),
            Math.Max(1, pageNumber) > 1,
            false,
            false,
            false,
            false,
            diagnostic is null ? Array.Empty<MediaLibraryQueryWarning>() : new[] { diagnostic },
            warning);
}

public interface IMediaLibraryQueryService
{
    Task<MediaLibraryQueryResult> SearchAsync(MediaLibraryQuery query, CancellationToken cancellationToken);
}
