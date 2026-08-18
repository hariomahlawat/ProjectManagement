namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaAlbumActor(string UserId, bool CanManageAnyAlbum)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(UserId);
}

public sealed record MediaAlbumListQuery(
    string? Query,
    string Sort,
    int PageNumber,
    int PageSize,
    bool IncludeArchived,
    MediaAlbumActor Actor);

public sealed record MediaAlbumSummary(
    Guid Id,
    string Name,
    string? Description,
    int ItemCount,
    int PhotoCount,
    int VideoCount,
    long? CoverMediaAssetId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string CreatedByUserId,
    bool IsArchived,
    bool CanManage);

public sealed record MediaAlbumPage(
    IReadOnlyList<MediaAlbumSummary> Albums,
    int TotalAlbums,
    int TotalVisibleItems,
    int TotalPhotos,
    int TotalVideos,
    int PageNumber,
    int PageSize,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record MediaAlbumOption(
    Guid Id,
    string Name,
    int ItemCount,
    bool IsOwner);

public sealed record MediaAlbumDetails(
    Guid Id,
    string Name,
    string? Description,
    string CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsArchived,
    long? CoverMediaAssetId,
    Guid ConcurrencyToken,
    bool CanManage,
    IReadOnlyList<long> OrderedVisibleAssetIds,
    int TotalMembershipCount,
    int ItemCount,
    int PhotoCount,
    int VideoCount);

public enum MediaAlbumMutationFailure
{
    None = 0,
    InvalidRequest = 1,
    NotFound = 2,
    Forbidden = 3,
    DuplicateName = 4,
    CapacityExceeded = 5,
    NoEligibleMedia = 6,
    ConcurrencyConflict = 7
}

public sealed record MediaAlbumMutationResult(
    bool Succeeded,
    MediaAlbumMutationFailure Failure,
    string Message,
    Guid? AlbumId = null,
    int AffectedCount = 0)
{
    public static MediaAlbumMutationResult Success(Guid albumId, string message, int affectedCount = 0)
        => new(true, MediaAlbumMutationFailure.None, message, albumId, affectedCount);

    public static MediaAlbumMutationResult Failed(MediaAlbumMutationFailure failure, string message, Guid? albumId = null)
        => new(false, failure, message, albumId);
}

public interface IMediaAlbumService
{
    Task<MediaAlbumPage> SearchAsync(MediaAlbumListQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaAlbumOption>> GetManageableOptionsAsync(
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumDetails?> GetDetailsAsync(
        Guid albumId,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumMutationResult> CreateAsync(
        string name,
        string? description,
        IReadOnlyCollection<long> assetIds,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumMutationResult> AddItemsAsync(
        Guid albumId,
        IReadOnlyCollection<long> assetIds,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumMutationResult> RemoveItemsAsync(
        Guid albumId,
        IReadOnlyCollection<long> assetIds,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumMutationResult> SetCoverAsync(
        Guid albumId,
        long assetId,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumMutationResult> ReorderAsync(
        Guid albumId,
        IReadOnlyList<long> orderedAssetIds,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumMutationResult> UpdateMetadataAsync(
        Guid albumId,
        string name,
        string? description,
        Guid concurrencyToken,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumMutationResult> SetArchivedAsync(
        Guid albumId,
        bool archived,
        Guid concurrencyToken,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);

    Task<MediaAlbumMutationResult> UpdateEditorialCaptionAsync(
        long assetId,
        string? caption,
        Guid? concurrencyToken,
        MediaAlbumActor actor,
        CancellationToken cancellationToken);
}
