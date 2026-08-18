namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Creates a bounded, validated ZIP archive for Photos bulk export. The service builds
/// the archive completely outside the HTTP response stream so ZipArchive can finalise
/// synchronously against a local file without requiring AllowSynchronousIO.
/// </summary>
public interface IMediaBulkDownloadService
{
    Task<MediaBulkDownloadResult> CreateAsync(
        IReadOnlyCollection<long> assetIds,
        string requestedByUserId,
        CancellationToken cancellationToken);
}

public enum MediaBulkDownloadFailureReason
{
    None = 0,
    EmptySelection = 1,
    TooManyItems = 2,
    SourceBytesExceeded = 3,
    NoEligibleAssets = 4,
    NoReadableAssets = 5,
    SourceReadFailed = 6
}

public sealed record MediaBulkDownloadArchive(
    Stream Stream,
    string FileName,
    long Length,
    int RequestedCount,
    int EligibleCount,
    int IncludedCount,
    int SkippedCount,
    long SourceBytes);

public sealed record MediaBulkDownloadResult(
    MediaBulkDownloadArchive? Archive,
    MediaBulkDownloadFailureReason FailureReason,
    string? Message,
    int RequestedCount,
    int EligibleCount,
    int IncludedCount,
    int SkippedCount,
    long SourceBytes)
{
    public bool Succeeded => Archive is not null && FailureReason == MediaBulkDownloadFailureReason.None;

    public static MediaBulkDownloadResult Success(MediaBulkDownloadArchive archive)
        => new(
            archive,
            MediaBulkDownloadFailureReason.None,
            null,
            archive.RequestedCount,
            archive.EligibleCount,
            archive.IncludedCount,
            archive.SkippedCount,
            archive.SourceBytes);

    public static MediaBulkDownloadResult Failure(
        MediaBulkDownloadFailureReason reason,
        string message,
        int requestedCount,
        int eligibleCount = 0,
        int includedCount = 0,
        int skippedCount = 0,
        long sourceBytes = 0)
        => new(
            null,
            reason,
            message,
            requestedCount,
            eligibleCount,
            includedCount,
            skippedCount,
            sourceBytes);
}
