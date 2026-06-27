using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaFileMetadata(
    MediaAssetKind Kind,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset FileModifiedAtUtc,
    DateTimeOffset MediaDateUtc,
    int? Width,
    int? Height,
    int? DurationSeconds,
    bool HasCameraMetadata,
    string? CameraMake,
    string? CameraModel);

public sealed record MediaClassificationResult(
    MediaClassification Classification,
    double Confidence,
    IReadOnlyList<string> Signals,
    string Version);

public interface IMediaSourceBootstrapper
{
    Task EnsureConfiguredSourcesAsync(CancellationToken cancellationToken);
}

public interface IPrismMediaCatalogueSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken);
}

public interface INetworkMediaSourceScanner
{
    Task ScanAsync(Guid sourceId, CancellationToken cancellationToken);
}

public interface INetworkSharePathResolver
{
    string ResolveRoot(string configuredRoot);
    string ResolveAssetPath(string rootPath, string relativePath);
    string ToRelativePath(string rootPath, string fullPath);
}

public interface IMediaMetadataReader
{
    Task<MediaFileMetadata> ReadAsync(string path, CancellationToken cancellationToken);
}

public interface IMediaClassifier
{
    Task<MediaClassificationResult> ClassifyAsync(
        string path,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken);
}

public interface IMediaCachePathResolver
{
    string CacheRoot { get; }
    string GetThumbnailPath(long assetId, int cacheVersion);
    string GetPreviewPath(long assetId, int cacheVersion);
}

public interface IMediaDerivativeService
{
    Task<string> EnsureAsync(long assetId, string variant, CancellationToken cancellationToken);
}

public interface IMediaAssetProcessor
{
    Task ProcessAsync(long assetId, MediaProcessingJobType jobType, CancellationToken cancellationToken);
}
