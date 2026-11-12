using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaLibraryQuery(
    string? Query,
    string Source,
    string Kind,
    string Classification,
    int? ProjectId,
    Guid? PersonId,
    int? Year,
    int PageNumber,
    int PageSize,
    bool IncludePeople,
    IReadOnlyList<Guid>? PersonIds = null,
    string PeopleMatch = "all",
    bool IncludeUnidentifiedFaces = false,
    string Sort = "newest",
    string? CollectionKey = null,
    IReadOnlyList<long>? AssetIds = null,
    bool IncludeMonths = false,
    // What only the full page shows: the figures, the year list, the project list and the
    // people list. A part of the timeline loaded while scrolling asks for the rows alone.
    // The rows, their order, paging and visibility are the same either way.
    bool IncludeFilterFacets = true);

/// <summary>How many items of the filtered result fall in one calendar month.</summary>
public sealed record MediaLibraryMonthCount(int Year, int Month, int Count);

public sealed record MediaLibraryPersonSummary(Guid Id, string DisplayName);
public sealed record MediaLibraryAlbumSummary(Guid Id, string Name);

public sealed record MediaLibraryQueryItem(
    long Id,
    Guid SourceId,
    MediaAssetOrigin Origin,
    MediaAssetKind Kind,
    MediaClassification Classification,
    string SourceEntityId,
    string? ParentEntityId,
    string ContextKey,
    string CollectionKey,
    string ContextTitle,
    string ContextSubtitle,
    string SourceLabel,
    string Title,
    string? Caption,
    string? EditorialCaption,
    Guid EditorialConcurrencyToken,
    string OriginalFileName,
    long FileSizeBytes,
    DateTimeOffset MediaDateUtc,
    int? Width,
    int? Height,
    int? DurationSeconds,
    bool IsCover,
    long SortOrder,
    int CacheVersion,
    string? VersionToken,
    IReadOnlyList<MediaLibraryPersonSummary> People,
    int UnidentifiedFaceCount,
    IReadOnlyList<MediaLibraryAlbumSummary> Albums);

public sealed record MediaLibraryProjectOption(int Id, string Name);
public sealed record MediaLibraryPersonOption(Guid Id, string Name, int PhotoCount, Guid? RepresentativeFaceId = null);
public sealed record MediaLibraryStatistics(int Total, int Photos, int Videos, int Collections);

public enum MediaLibraryQueryOperation
{
    CatalogueDisabled,
    PrimaryTimeline,
    Statistics,
    Years,
    Projects,
    People,
    PrismSourceStatus,
    Months
}

public sealed record MediaLibraryQueryWarning(
    MediaLibraryQueryOperation Operation,
    string Reference,
    string Message,
    DateTimeOffset OccurredAtUtc);

public sealed record MediaLibraryQueryResult(
    IReadOnlyList<MediaLibraryQueryItem> Items,
    IReadOnlyList<MediaLibraryProjectOption> Projects,
    IReadOnlyList<MediaLibraryPersonOption> People,
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

    /// <summary>Per-month counts of the filtered result; empty unless the query asked for them.</summary>
    public IReadOnlyList<MediaLibraryMonthCount> Months { get; init; } = Array.Empty<MediaLibraryMonthCount>();

    public static MediaLibraryQueryResult Unavailable(
        int pageNumber,
        int pageSize,
        string warning,
        MediaLibraryQueryWarning? diagnostic = null)
        => new(
            Array.Empty<MediaLibraryQueryItem>(),
            Array.Empty<MediaLibraryProjectOption>(),
            Array.Empty<MediaLibraryPersonOption>(),
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
    Task<MediaLibraryQueryResult> SearchAsync(
        MediaLibraryQuery query,
        CancellationToken cancellationToken);
}
