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

/// <summary>
/// Readiness for detector-only classification assistance. It deliberately excludes the
/// People feature switch, face worker, embedding model, database schema and identity cache.
/// </summary>
public sealed record FaceDetectorReadiness(
    bool IsEnabled,
    bool IsReady,
    FaceReadinessState State,
    string Message,
    string? DetectorPath,
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

public sealed record FaceCandidate(
    Guid PersonId,
    string DisplayName,
    Guid? RepresentativeFaceId,
    double Similarity,
    double BestReferenceSimilarity = 0d,
    double MeanTopSimilarity = 0d,
    int ReferenceCount = 0,
    double? MarginToNext = null,
    bool MarginAvailable = false,
    FaceCandidateConfidenceLevel ConfidenceLevel = FaceCandidateConfidenceLevel.None);

public sealed record FaceCandidateSearchInput(
    Guid FaceId,
    long AssetId,
    float[] Embedding,
    string ModelKey,
    string ModelVersion,
    int Dimension);

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
    Task<FaceDetectorReadiness> CheckDetectorAsync(CancellationToken cancellationToken);
    Task<FaceDetectorReadiness> CheckDetectorAsync(bool forceRefresh, CancellationToken cancellationToken);
}

public interface IFacePresenceAnalysisEngine
{
    Task<FacePresenceResult> AnalysePresenceAsync(byte[] imageBytes, CancellationToken cancellationToken);
}

/// <summary>
/// The largest face in a photo: its box and its five points, in the photo's own pixels. The
/// points are in the detector's order (right eye, left eye, nose tip, right and left corner of
/// the mouth, as seen in the photo).
/// </summary>
public sealed record LocatedFace(
    double Left,
    double Top,
    double Width,
    double Height,
    double Confidence,
    IReadOnlyList<FacePoint> Points);

/// <summary>The photo's size, and its largest face or null when there is none.</summary>
public sealed record FaceLocation(int ImageWidth, int ImageHeight, LocatedFace? Face);

/// <summary>
/// Finds where a face is, with the detector only: no recogniser, no thumbnail, no record. Used by
/// Photo Tools to place a passport photo; independent of People.
/// </summary>
public interface IFaceLocationEngine
{
    Task<FaceDetectorReadiness> DetectorReadinessAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The largest face. With <paramref name="waitForTurn"/>, waits at most that long for the
    /// detector when the Photos library is using it, then throws <see cref="TimeoutException"/>.
    /// </summary>
    Task<FaceLocation> LocateLargestFaceAsync(byte[] imageBytes, CancellationToken cancellationToken, TimeSpan? waitForTurn = null);
}

public interface IFaceAnalysisEngine
{
    Task<IReadOnlyList<DetectedFaceData>> AnalyseAsync(byte[] imageBytes, CancellationToken cancellationToken);
}

public interface IFaceIntelligenceService
{
    Task ProcessAssetAsync(long assetId, CancellationToken cancellationToken);

    /// <summary>
    /// Regenerates current-model embeddings for existing detected faces while preserving
    /// face identities, bounding boxes and human-reviewed assignments.
    /// </summary>
    Task<int> RefreshEmbeddingsAsync(long assetId, CancellationToken cancellationToken);
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

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<FaceCandidate>>> SearchBatchAsync(
        IReadOnlyCollection<FaceCandidateSearchInput> inputs,
        CancellationToken cancellationToken);

    /// <summary>
    /// Batch search where the caller decides which people to exclude for each input face,
    /// instead of the rejections recorded on that face. Identity grouping searches with a
    /// group centroid on behalf of every member: a rejection on the representative alone
    /// must not remove a person for the whole group, while a person rejected for every
    /// member must be excluded before results are ranked and limited.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<FaceCandidate>>> SearchBatchAsync(
        IReadOnlyCollection<FaceCandidateSearchInput> inputs,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> excludedPeopleByInputFace,
        CancellationToken cancellationToken)
        => SearchBatchAsync(inputs, cancellationToken);
}

public interface IFaceCandidateSuggestionService
{
    Task<int> RefreshFaceAsync(Guid faceId, CancellationToken cancellationToken);
    Task<int> RefreshUnassignedAsync(int limit, CancellationToken cancellationToken);
}

public interface IFaceCandidateRefreshQueueService
{
    Task<bool> QueueFaceAsync(Guid faceId, CancellationToken cancellationToken);
    Task<int> QueueFacesAsync(
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken);
    Task<int> QueueAllUnassignedAsync(CancellationToken cancellationToken);
    Task<int> RecoverStaleProcessingAsync(CancellationToken cancellationToken);
}

public sealed record FaceCandidateRefreshRuntimeSnapshot(
    bool WorkerConfigured,
    bool WorkerStarted,
    string State,
    string WorkerId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? CurrentBatchStartedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    DateTimeOffset? LastFailedAtUtc,
    int LastBatchProcessedCount,
    int ProcessedSinceStart,
    int FailureCountSinceStart,
    int RecoveredStaleSinceStart,
    string? LastFailureCode,
    string? LastFailureMessage);

public interface IFaceCandidateRefreshRuntimeState
{
    void MarkConfigured(bool configured);
    void MarkStarted(string workerId);
    void Heartbeat(string state);
    void MarkBatchStarted();
    void MarkBatchCompleted(int processedCount);
    void MarkFailed(Exception exception);
    void MarkRecovered(int recoveredCount);
    void MarkIdle();
    void RequestRun();
    Task WaitForRunRequestAsync(TimeSpan maximumDelay, CancellationToken cancellationToken);
    FaceCandidateRefreshRuntimeSnapshot GetSnapshot();
}

public sealed record FaceIdentityGroupMember(
    Guid FaceId,
    long AssetId,
    string ContextTitle,
    string ContextSubtitle,
    DateTimeOffset MediaDateUtc,
    double QualityScore,
    double SimilarityToRepresentative);

public sealed record FaceIdentityGroup(
    string GroupKey,
    Guid RepresentativeFaceId,
    IReadOnlyList<Guid> FaceIds,
    IReadOnlyList<FaceIdentityGroupMember> Members,
    IReadOnlyList<FaceCandidate> Candidates,
    double CohesionScore,
    int PhotoCount,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public sealed record FaceIdentityGroupingResult(
    IReadOnlyList<FaceIdentityGroup> Groups,
    int TotalGroups,
    int GroupedFaceCount,
    int UngroupedFaceCount);

public interface IFaceIdentityGroupingService
{
    Task<FaceIdentityGroupingResult> GetGroupsAsync(CancellationToken cancellationToken);
}

public sealed record FaceIdentityGroupingRuntimeSnapshot(
    FaceIdentityGroupingResult? Result,
    DateTimeOffset? RefreshedAtUtc,
    string? FailureReason,
    bool IsRefreshPending = false,
    DateTimeOffset? InvalidatedAtUtc = null,
    long RefreshGeneration = 0)
{
    public bool IsReady => Result is not null;
}

public interface IFaceIdentityGroupingRuntimeState
{
    FaceIdentityGroupingRuntimeSnapshot GetSnapshot();
    void SetResult(
        FaceIdentityGroupingResult result,
        DateTimeOffset refreshedAtUtc,
        long? refreshGeneration = null);
    void SetFailure(string failureReason, DateTimeOffset failedAtUtc);
    void Invalidate();
    Task WaitForRefreshRequestAsync(TimeSpan maximumDelay, CancellationToken cancellationToken);
}

public interface IFaceReviewService
{
    Task<Guid> CreatePersonAndAssignAsync(
        Guid faceId,
        string displayName,
        string userId,
        CancellationToken cancellationToken);

    Task<Guid> CreatePersonAndAssignManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string displayName,
        string userId,
        CancellationToken cancellationToken);

    Task AssignAsync(
        Guid faceId,
        Guid personId,
        string userId,
        double? confidence,
        CancellationToken cancellationToken);

    Task AssignManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        double? confidence,
        CancellationToken cancellationToken,
        string reviewSource = "IdentityReview");

    Task ConfirmCandidateAsync(
        Guid faceId,
        Guid personId,
        string userId,
        CancellationToken cancellationToken);

    Task ConfirmCandidateManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        CancellationToken cancellationToken);

    Task RejectCandidateAsync(
        Guid faceId,
        Guid personId,
        string userId,
        CancellationToken cancellationToken);

    Task RejectCandidateManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        CancellationToken cancellationToken);

    Task RejectAsync(
        Guid faceId,
        Guid? personId,
        string userId,
        CancellationToken cancellationToken);

    Task RejectManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        CancellationToken cancellationToken,
        string reviewSource = "IdentityReview");

    Task IgnoreAsync(
        Guid faceId,
        string userId,
        CancellationToken cancellationToken);

    Task IgnoreManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        CancellationToken cancellationToken);

    Task ReopenUnidentifiedAsync(
        Guid faceId,
        string userId,
        CancellationToken cancellationToken);

    Task ReopenUnidentifiedManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        CancellationToken cancellationToken);

    Task SuppressAsync(Guid faceId, string userId, CancellationToken cancellationToken);

    Task SuppressManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        CancellationToken cancellationToken);

    Task SuppressAsync(
        Guid faceId,
        string userId,
        string reason,
        CancellationToken cancellationToken);

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

    Task SetReferenceStatusAsync(
        Guid personId,
        Guid faceId,
        FaceReferenceStatus referenceStatus,
        string userId,
        string reason,
        CancellationToken cancellationToken);

    Task RemoveAssignmentAsync(
        Guid faceId,
        Guid personId,
        string userId,
        string? reason,
        CancellationToken cancellationToken);

    Task MoveAssignmentsAsync(
        Guid sourcePersonId,
        IReadOnlyCollection<Guid> faceIds,
        Guid targetPersonId,
        string userId,
        string reason,
        CancellationToken cancellationToken);

    Task<Guid> SplitToNewPersonAsync(
        Guid sourcePersonId,
        IReadOnlyCollection<Guid> faceIds,
        string displayName,
        string userId,
        string reason,
        CancellationToken cancellationToken);

    Task ReturnAssignmentsToReviewAsync(
        Guid sourcePersonId,
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// The faces among <paramref name="faceIds"/> whose active assignment to the person was made
    /// by this user at or after <paramref name="sinceUtc"/>. Naming a face someone else has
    /// already given to the same person succeeds without assigning anything; this tells the two
    /// apart, so that only what a request itself assigned can be taken back.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetFacesAssignedByAsync(
        Guid personId,
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// The faces among <paramref name="faceIds"/> that this user left unnamed at or after
    /// <paramref name="sinceUtc"/>. Leaving unnamed a face someone else has already left unnamed
    /// succeeds without recording anything new; this tells the two apart, so that only what a
    /// request itself closed can be reopened by its Undo.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetFacesLeftUnnamedByAsync(
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken);

    Task MergePeopleAsync(
        Guid sourcePersonId,
        Guid targetPersonId,
        string userId,
        string reason,
        CancellationToken cancellationToken);
}
