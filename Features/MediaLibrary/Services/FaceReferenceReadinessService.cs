using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public enum FaceReferenceReadinessCode
{
    Eligible = 0,
    AlreadyTrusted = 1,
    MediaUnavailable = 2,
    FaceSuppressed = 3,
    QualityTooLow = 4,
    QualityNotEligible = 5,
    EmbeddingMissing = 6,
    EmbeddingOutdated = 7,
    PreparationPending = 8,
    PreparationFailed = 9,
    FaceProcessingNotAllowed = 10,
    AssignmentUnavailable = 11
}

public sealed record FaceReferenceReadiness(
    Guid FaceId,
    long AssetId,
    FaceReferenceReadinessCode Code,
    bool CanTrust,
    bool CanPrepare,
    bool IsPreparationPending,
    bool IsTrusted,
    string Label,
    string Message,
    string? FailureReason = null)
{
    public bool IsUsableReference
        => CanTrust || (IsTrusted && Code == FaceReferenceReadinessCode.AlreadyTrusted);
}

public interface IFaceReferenceReadinessService
{
    Task<FaceReferenceReadiness> GetAsync(
        Guid personId,
        Guid faceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, FaceReferenceReadiness>> GetManyAsync(
        Guid personId,
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken);

    Task<FaceReferenceReadiness> QueuePreparationAsync(
        Guid personId,
        Guid faceId,
        string userId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Single source of truth for whether a confirmed appearance may become trusted biometric
/// matching evidence. The identity-governance UI and mutation service consume the same
/// evaluation so an action cannot be presented as valid and then rejected by a different
/// server rule. Missing or stale embeddings are repaired through the durable media job queue
/// without replacing confirmed faces or human-reviewed assignments.
/// </summary>
public sealed class FaceReferenceReadinessService : IFaceReferenceReadinessService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IFaceEligibilityPolicy _faceEligibility;
    private readonly MediaLibraryOptions _options;

    public FaceReferenceReadinessService(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IFaceEligibilityPolicy faceEligibility,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _faceEligibility = faceEligibility ?? throw new ArgumentNullException(nameof(faceEligibility));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<FaceReferenceReadiness> GetAsync(
        Guid personId,
        Guid faceId,
        CancellationToken cancellationToken)
    {
        var result = await GetManyAsync(personId, new[] { faceId }, cancellationToken);
        return result.GetValueOrDefault(faceId) ?? Missing(faceId);
    }

    public async Task<IReadOnlyDictionary<Guid, FaceReferenceReadiness>> GetManyAsync(
        Guid personId,
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(faceIds);
        var selected = faceIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(250)
            .ToArray();
        if (personId == Guid.Empty || selected.Length == 0)
        {
            return new Dictionary<Guid, FaceReferenceReadiness>();
        }

        var assignments = await _db.PersonFaces
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.MediaFace)
                .ThenInclude(face => face.MediaAsset)
                    .ThenInclude(asset => asset.Source)
            .Include(item => item.MediaFace)
                .ThenInclude(face => face.MediaAsset)
                    .ThenInclude(asset => asset.ProcessingJobs)
            .Include(item => item.MediaFace)
                .ThenInclude(face => face.Embeddings)
            .Where(item => item.MediaPersonId == personId
                           && selected.Contains(item.MediaFaceId)
                           && item.RemovedAtUtc == null)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, FaceReferenceReadiness>();
        foreach (var assignment in assignments)
        {
            result[assignment.MediaFaceId] = Evaluate(assignment);
        }

        foreach (var faceId in selected)
        {
            result.TryAdd(faceId, Missing(faceId));
        }

        return result;
    }

    public async Task<FaceReferenceReadiness> QueuePreparationAsync(
        Guid personId,
        Guid faceId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A reviewer identity is required.", nameof(userId));
        }

        var readiness = await GetAsync(personId, faceId, cancellationToken);
        if (readiness.IsPreparationPending)
        {
            return readiness;
        }
        if (!readiness.CanPrepare)
        {
            throw new FaceIdentityConflictException(readiness.Message);
        }

        var asset = await _db.Assets
            .Include(item => item.ProcessingJobs)
            .SingleOrDefaultAsync(item => item.Id == readiness.AssetId, cancellationToken)
            ?? throw new FaceIdentityConflictException("The source photograph is no longer available.");

        var now = DateTimeOffset.UtcNow;
        var job = asset.ProcessingJobs
            .Where(item => item.JobType == MediaProcessingJobType.GenerateFaceEmbeddings)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();

        if (job is null)
        {
            job = new MediaProcessingJob
            {
                MediaAssetId = asset.Id,
                JobType = MediaProcessingJobType.GenerateFaceEmbeddings,
                Status = MediaProcessingJobStatus.Pending,
                AttemptCount = 0,
                MaxAttempts = Math.Max(3, _options.Processing.MaxAttempts),
                AvailableAfterUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.ProcessingJobs.Add(job);
        }
        else if (job.Status is not (MediaProcessingJobStatus.Pending or MediaProcessingJobStatus.Running))
        {
            job.Status = MediaProcessingJobStatus.Pending;
            job.AttemptCount = 0;
            job.MaxAttempts = Math.Max(3, _options.Processing.MaxAttempts);
            job.AvailableAfterUtc = now;
            job.StartedAtUtc = null;
            job.CompletedAtUtc = null;
            job.LockedBy = null;
            job.LockExpiresAtUtc = null;
            job.FailureCode = null;
            job.FailureMessage = null;
            job.UpdatedAtUtc = now;
        }

        asset.FaceAnalysisStatus = MediaProcessingStatus.Pending;
        asset.FaceProcessingFailureReason = null;
        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            FaceId = faceId,
            PersonId = personId,
            Action = "ReferencePreparationQueued",
            PerformedByUserId = userId.Trim(),
            Notes = "Queued current-model face embedding preparation for a confirmed appearance.",
            PerformedAtUtc = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        return readiness with
        {
            Code = FaceReferenceReadinessCode.PreparationPending,
            CanPrepare = false,
            IsPreparationPending = true,
            Label = "Preparing embedding",
            Message = "PRISM queued this photograph for current-model face embedding preparation. Refresh after background processing completes."
        };
    }

    private FaceReferenceReadiness Evaluate(MediaPersonFace assignment)
    {
        var face = assignment.MediaFace;
        var asset = face.MediaAsset;
        var people = _options.People;
        var hasCurrentEmbedding = face.Embeddings.Any(item =>
            item.InvalidatedAtUtc == null
            && item.ModelKey == people.Embedder.Key
            && item.ModelVersion == people.Embedder.Version
            && item.Dimension == people.Embedder.EmbeddingDimension
            && item.Embedding.Length > 0);
        var hasAnyActiveEmbedding = face.Embeddings.Any(item =>
            item.InvalidatedAtUtc == null && item.Embedding.Length > 0);
        var latestPreparation = asset.ProcessingJobs
            .Where(item => item.JobType == MediaProcessingJobType.GenerateFaceEmbeddings)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();
        var isTrusted = assignment.ReferenceStatus == FaceReferenceStatus.TrustedReference;

        FaceReferenceReadiness Result(
            FaceReferenceReadinessCode code,
            bool canTrust,
            bool canPrepare,
            bool pending,
            string label,
            string message,
            string? failure = null)
            => new(face.Id, asset.Id, code, canTrust, canPrepare, pending, isTrusted, label, message, failure);

        if (!_visibility.IsVisible(asset))
        {
            return Result(FaceReferenceReadinessCode.MediaUnavailable, false, false, false,
                "Source unavailable", "The source photograph is not currently available in Photos.");
        }
        if (face.IsSuppressed)
        {
            return Result(FaceReferenceReadinessCode.FaceSuppressed, false, false, false,
                "Detection retired", "This detection has been marked as not a valid face and cannot be used for matching.");
        }
        if (face.QualityScore < people.CandidateMinimumTrustedReferenceQuality)
        {
            return Result(FaceReferenceReadinessCode.QualityTooLow, false, false, false,
                "Quality below reference threshold",
                $"Face quality {face.QualityScore:P0} is below the configured {people.CandidateMinimumTrustedReferenceQuality:P0} trusted-reference threshold. Choose another appearance.");
        }
        if (face.QualityStatus is FaceQualityStatus.LowResolution
            or FaceQualityStatus.Blurred
            or FaceQualityStatus.PoorExposure
            or FaceQualityStatus.ExtremePose
            or FaceQualityStatus.Occluded)
        {
            return Result(FaceReferenceReadinessCode.QualityNotEligible, false, false, false,
                "Face not embedding-eligible",
                $"This appearance is classified as {Humanize(face.QualityStatus)} and is not suitable as trusted matching evidence. Choose another clear appearance.");
        }

        if (hasCurrentEmbedding && face.QualityStatus == FaceQualityStatus.EmbeddingEligible)
        {
            return isTrusted
                ? Result(FaceReferenceReadinessCode.AlreadyTrusted, false, false, false,
                    "Trusted reference ready", "This appearance is a current, usable trusted matching reference.")
                : Result(FaceReferenceReadinessCode.Eligible, true, false, false,
                    "Ready to trust", "Current face quality and embedding satisfy the trusted-reference policy.");
        }

        if (latestPreparation?.Status is MediaProcessingJobStatus.Pending or MediaProcessingJobStatus.Running)
        {
            return Result(FaceReferenceReadinessCode.PreparationPending, false, false, true,
                "Preparing embedding", "PRISM is preparing a current matching embedding for this confirmed appearance.");
        }

        var faceProcessing = _faceEligibility.Evaluate(asset);
        if (!faceProcessing.IsEligible)
        {
            return Result(FaceReferenceReadinessCode.FaceProcessingNotAllowed, false, false, false,
                "Reprocessing unavailable", faceProcessing.Reason);
        }

        if (latestPreparation?.Status is MediaProcessingJobStatus.Failed or MediaProcessingJobStatus.DeadLetter)
        {
            var failure = latestPreparation.FailureMessage ?? asset.FaceProcessingFailureReason;
            return Result(FaceReferenceReadinessCode.PreparationFailed, false, true, false,
                "Embedding preparation failed",
                "The previous embedding-preparation attempt did not complete. You can retry this appearance.", failure);
        }

        if (face.QualityStatus is not (FaceQualityStatus.EmbeddingEligible
            or FaceQualityStatus.Detected
            or FaceQualityStatus.ProcessingFailed))
        {
            return Result(FaceReferenceReadinessCode.QualityNotEligible, false, false, false,
                "Face not embedding-eligible",
                $"This appearance has quality state {Humanize(face.QualityStatus)} and cannot currently become a trusted reference.");
        }

        return hasAnyActiveEmbedding
            ? Result(FaceReferenceReadinessCode.EmbeddingOutdated, false, true, false,
                "Embedding needs refresh",
                "This appearance has face data from an older or incompatible model. Prepare a current embedding before trusting it for matching.")
            : Result(FaceReferenceReadinessCode.EmbeddingMissing, false, true, false,
                "Embedding not available",
                "This confirmed face does not yet have a current matching embedding. Prepare it before using it as a trusted reference.");
    }

    private static FaceReferenceReadiness Missing(Guid faceId)
        => new(faceId, 0, FaceReferenceReadinessCode.AssignmentUnavailable, false, false, false, false,
            "Appearance unavailable", "This appearance is no longer actively assigned to this person.");

    private static string Humanize(FaceQualityStatus status)
        => status switch
        {
            FaceQualityStatus.LowResolution => "low resolution",
            FaceQualityStatus.Blurred => "blurred",
            FaceQualityStatus.PoorExposure => "poor exposure",
            FaceQualityStatus.ExtremePose => "extreme pose",
            FaceQualityStatus.Occluded => "occluded",
            FaceQualityStatus.ProcessingFailed => "processing failed",
            FaceQualityStatus.EmbeddingEligible => "embedding eligible",
            FaceQualityStatus.Detected => "detected",
            FaceQualityStatus.Suppressed => "suppressed",
            _ => status.ToString()
        };
}
