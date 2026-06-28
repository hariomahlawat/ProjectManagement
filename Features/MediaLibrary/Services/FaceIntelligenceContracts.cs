using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public enum FaceReadinessState
{
    Disabled = 0,
    ConfigurationIncomplete = 1,
    ModelsMissing = 2,
    ChecksumMismatch = 3,
    LicenceUnverified = 4,
    RuntimeUnavailable = 5,
    SchemaUnavailable = 6,
    CacheUnavailable = 7,
    Ready = 8,
    Degraded = 9,
    ModelContractInvalid = 10
}

public sealed record FaceReadinessCheck(
    string Key,
    string Label,
    bool IsSatisfied,
    string Status,
    string? Action = null);

public sealed record FaceModelReadiness(
    bool IsEnabled,
    bool IsReady,
    FaceReadinessState State,
    string Message,
    string? DetectorPath,
    string? EmbedderPath,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<FaceReadinessCheck> Checks);

public sealed record FaceQualitySignals(
    double Resolution,
    double Sharpness,
    double Exposure,
    double Contrast,
    double Pose,
    double CropCompleteness,
    IReadOnlyList<string> Reasons);

public sealed record DetectedFaceData(
    double Left,
    double Top,
    double Width,
    double Height,
    double Confidence,
    double QualityScore,
    FaceQualityStatus QualityStatus,
    float[]? Embedding,
    IReadOnlyList<double>? Landmarks,
    byte[]? ReviewThumbnail,
    double? BlurScore = null,
    double? BrightnessScore = null,
    double? PoseScore = null,
    FaceQualitySignals? QualitySignals = null);

public sealed record FaceCandidate(Guid PersonId, string DisplayName, double Similarity);

public sealed record FaceReviewOperationResult(Guid? PersonId, Guid FaceId, string Message);

public sealed class FaceIdentityConflictException : InvalidOperationException
{
    public FaceIdentityConflictException(string message) : base(message)
    {
    }
}

public interface IFaceModelReadinessService
{
    Task<FaceModelReadiness> CheckAsync(CancellationToken cancellationToken);
    Task<FaceModelReadiness> CheckAsync(bool forceRefresh, CancellationToken cancellationToken);
}

public interface IFaceAnalysisEngine
{
    Task<IReadOnlyList<DetectedFaceData>> AnalyseAsync(byte[] imageBytes, CancellationToken cancellationToken);
}

public interface IFaceIntelligenceService
{
    Task ProcessAssetAsync(long assetId, CancellationToken cancellationToken);
}

public interface IFaceQueueService
{
    Task<int> QueueEligibleAsync(int limit, CancellationToken cancellationToken);
    Task<bool> QueueAssetAsync(long assetId, CancellationToken cancellationToken);
}

public interface IFaceCandidateSearchService
{
    Task<IReadOnlyList<FaceCandidate>> SearchAsync(
        Guid faceId,
        float[] embedding,
        string modelKey,
        string modelVersion,
        int dimension,
        CancellationToken cancellationToken);
}

public interface IFaceReviewService
{
    Task<Guid> CreatePersonAndAssignAsync(
        Guid faceId,
        string displayName,
        string userId,
        CancellationToken cancellationToken);

    Task AssignAsync(
        Guid faceId,
        Guid personId,
        string userId,
        double? confidence,
        CancellationToken cancellationToken);

    Task RejectAsync(
        Guid faceId,
        Guid? personId,
        string userId,
        CancellationToken cancellationToken);

    Task IgnoreAsync(
        Guid faceId,
        string userId,
        CancellationToken cancellationToken);

    Task SuppressAsync(Guid faceId, string userId, CancellationToken cancellationToken);

    Task RenamePersonAsync(
        Guid personId,
        string displayName,
        string userId,
        CancellationToken cancellationToken);

    Task SetPersonHiddenAsync(
        Guid personId,
        bool hidden,
        string userId,
        CancellationToken cancellationToken);

    Task SetRepresentativeFaceAsync(
        Guid personId,
        Guid faceId,
        string userId,
        CancellationToken cancellationToken);

    Task RemoveAssignmentAsync(
        Guid faceId,
        Guid personId,
        string userId,
        string? reason,
        CancellationToken cancellationToken);

    Task MergePeopleAsync(
        Guid sourcePersonId,
        Guid targetPersonId,
        string userId,
        CancellationToken cancellationToken);
}
