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

public sealed record ClassificationMetrics(
    double Entropy,
    double EdgeDensity,
    double SpatialFlatness,
    double LightBackgroundRatio,
    double ColourDiversity,
    double LuminanceVariance,
    double AspectRatio,
    int Width,
    int Height);

public sealed record FacePresenceResult(
    bool Succeeded,
    bool FaceDetected,
    int FaceCount,
    double HighestConfidence,
    int LargestFaceWidth,
    int LargestFaceHeight,
    double LargestFaceAreaRatio,
    bool ValidFivePointLandmarks,
    string? FailureReason = null);

public sealed record MediaClassificationResult(
    MediaClassification PredictedClassification,
    double PredictedScore,
    IReadOnlyDictionary<MediaClassification, double> CategoryScores,
    IReadOnlyList<string> Signals,
    ClassificationMetrics Metrics,
    MediaClassification EffectiveClassification,
    MediaClassificationDecisionStatus DecisionStatus,
    string DecisionReasonCode,
    string Version,
    int ProcessingDurationMilliseconds);

public sealed record MediaClassificationDecision(
    MediaClassification EffectiveClassification,
    MediaClassificationDecisionStatus Status,
    string ReasonCode);


public sealed record FileSystemSourceHealth(
    bool IsReachable,
    string PathKind,
    int SampleMediaCount,
    string Message,
    DateTimeOffset CheckedAtUtc);

public sealed record ExternalMediaSearchRequest(
    string? Query,
    string Kind,
    string Classification,
    int? Year,
    int Skip,
    int Take);

public sealed record ExternalMediaSearchItem(
    long Id,
    MediaAssetKind Kind,
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
    long SortOrder,
    int CacheVersion,
    string? ParentEntityId);

public sealed record ExternalMediaSearchResult(
    IReadOnlyList<ExternalMediaSearchItem> Items,
    int Total,
    int Photos,
    int Videos,
    int Collections,
    IReadOnlyList<int> Years,
    bool IsAvailable,
    string? Warning)
{
    public static ExternalMediaSearchResult Empty(bool available = true, string? warning = null)
        => new(Array.Empty<ExternalMediaSearchItem>(), 0, 0, 0, 0, Array.Empty<int>(), available, warning);
}

public interface IMediaSourceBootstrapper
{
    Task EnsureConfiguredSourcesAsync(CancellationToken cancellationToken);
}

public interface IPrismMediaCatalogueSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken);
}

public interface IExternalMediaSourceScanner
{
    Task ScanAsync(Guid sourceId, string workerId, CancellationToken cancellationToken);
}

public interface IFileSystemPathResolver
{
    string ResolveRoot(string configuredRoot);
    string ResolveAssetPath(string rootPath, string relativePath);
    string ToRelativePath(string rootPath, string fullPath);
    string DescribePathKind(string configuredRoot);
}

public interface IFileSystemSourceHealthService
{
    Task<FileSystemSourceHealth> TestAsync(
        string rootPath,
        bool includeSubfolders,
        IReadOnlyCollection<string> allowedExtensions,
        CancellationToken cancellationToken);
}

public interface IExternalMediaLibraryReader
{
    Task<ExternalMediaSearchResult> SearchAsync(
        ExternalMediaSearchRequest request,
        CancellationToken cancellationToken);
}

public interface IMediaMetadataReader
{
    Task<MediaFileMetadata> ReadAsync(string path, CancellationToken cancellationToken);
    Task<MediaFileMetadata> ReadAsync(MediaContentDescriptor content, CancellationToken cancellationToken);
}

public interface IFacePresenceProbe
{
    Task<FacePresenceResult> AnalyseAsync(byte[] imageBytes, CancellationToken cancellationToken);
}

public interface IMediaClassificationDecisionPolicy
{
    MediaClassificationDecision Decide(
        MediaClassification predictedClassification,
        double predictedScore,
        IReadOnlyDictionary<MediaClassification, double> categoryScores,
        IReadOnlyList<string> signals);
}

public interface IMediaClassifier
{
    Task<MediaClassificationResult> ClassifyAsync(
        string path,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken);

    Task<MediaClassificationResult> ClassifyAsync(
        MediaContentDescriptor content,
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
