using System.Text.Json;
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
    AssignmentUnavailable = 11,
    EligibleWithCaution = 12,
    CropTooIncomplete = 13
}

/// <summary>
/// Separates technical embedding availability from the stricter governance decision about
/// whether an appearance is suitable as trusted biometric evidence.
/// </summary>
public enum FaceReferenceSuitability
{
    NotUsable = 0,
    Preferred = 1,
    UsableWithCaution = 2
}

public sealed record FaceReferenceReadiness(
    Guid FaceId,
    long AssetId,
    FaceReferenceReadinessCode Code,
    bool CanTrust,
    bool CanPrepare,
    bool IsPreparationPending,
    bool IsTrusted,
    FaceReferenceSuitability Suitability,
    string Label,
    string Message,
    string? FailureReason = null)
{
    public bool IsUsableReference
        => IsTrusted
           && (Suitability is FaceReferenceSuitability.Preferred
               or FaceReferenceSuitability.UsableWithCaution)
           && Code == FaceReferenceReadinessCode.AlreadyTrusted;

    public bool RequiresCaution
        => Suitability == FaceReferenceSuitability.UsableWithCaution;
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
        var signals = ParseSignals(face.QualitySignalsJson);
        var quality = AssessReferenceQuality(face, signals, people.CandidateMinimumTrustedReferenceQuality);
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
            FaceReferenceSuitability suitability,
            string label,
            string message,
            string? failure = null)
            => new(
                face.Id,
                asset.Id,
                code,
                canTrust,
                canPrepare,
                pending,
                isTrusted,
                suitability,
                label,
                message,
                failure);

        if (!_visibility.IsVisible(asset))
        {
            return Result(
                FaceReferenceReadinessCode.MediaUnavailable,
                false,
                false,
                false,
                FaceReferenceSuitability.NotUsable,
                "Source unavailable",
                "The source photograph is not currently available in Photos.");
        }

        if (face.IsSuppressed)
        {
            return Result(
                FaceReferenceReadinessCode.FaceSuppressed,
                false,
                false,
                false,
                FaceReferenceSuitability.NotUsable,
                "Detection retired",
                "This detection has been marked as not a valid face and cannot be used for matching.");
        }

        if (quality.Suitability == FaceReferenceSuitability.NotUsable
            && !quality.CanReprocess)
        {
            return Result(
                quality.Code,
                false,
                false,
                false,
                FaceReferenceSuitability.NotUsable,
                quality.Label,
                quality.Message);
        }

        if (hasCurrentEmbedding
            && (quality.Suitability is FaceReferenceSuitability.Preferred
                or FaceReferenceSuitability.UsableWithCaution))
        {
            if (isTrusted)
            {
                return quality.Suitability == FaceReferenceSuitability.UsableWithCaution
                    ? Result(
                        FaceReferenceReadinessCode.AlreadyTrusted,
                        false,
                        false,
                        false,
                        quality.Suitability,
                        "Trusted with caution",
                        quality.Message)
                    : Result(
                        FaceReferenceReadinessCode.AlreadyTrusted,
                        false,
                        false,
                        false,
                        quality.Suitability,
                        "Trusted reference ready",
                        "This appearance is a current, preferred trusted matching reference.");
            }

            return quality.Suitability == FaceReferenceSuitability.UsableWithCaution
                ? Result(
                    FaceReferenceReadinessCode.EligibleWithCaution,
                    true,
                    false,
                    false,
                    quality.Suitability,
                    "Usable with caution",
                    quality.Message)
                : Result(
                    FaceReferenceReadinessCode.Eligible,
                    true,
                    false,
                    false,
                    quality.Suitability,
                    "Preferred reference",
                    "Current face quality and embedding satisfy the preferred trusted-reference policy.");
        }

        if (latestPreparation?.Status is MediaProcessingJobStatus.Pending or MediaProcessingJobStatus.Running)
        {
            return Result(
                FaceReferenceReadinessCode.PreparationPending,
                false,
                false,
                true,
                quality.Suitability,
                "Preparing embedding",
                "PRISM is preparing a current matching embedding for this confirmed appearance.");
        }

        var faceProcessing = _faceEligibility.Evaluate(asset);
        if (!faceProcessing.IsEligible)
        {
            return Result(
                FaceReferenceReadinessCode.FaceProcessingNotAllowed,
                false,
                false,
                false,
                FaceReferenceSuitability.NotUsable,
                "Reprocessing unavailable",
                faceProcessing.Reason);
        }

        if (latestPreparation?.Status is MediaProcessingJobStatus.Failed or MediaProcessingJobStatus.DeadLetter)
        {
            var failure = latestPreparation.FailureMessage ?? asset.FaceProcessingFailureReason;
            return Result(
                FaceReferenceReadinessCode.PreparationFailed,
                false,
                quality.CanReprocess,
                false,
                quality.Suitability,
                "Embedding preparation failed",
                quality.CanReprocess
                    ? "The previous embedding-preparation attempt did not complete. You can retry this appearance."
                    : quality.Message,
                failure);
        }

        if (!quality.CanReprocess)
        {
            return Result(
                quality.Code,
                false,
                false,
                false,
                FaceReferenceSuitability.NotUsable,
                quality.Label,
                quality.Message);
        }

        var preparationLabel = quality.Suitability == FaceReferenceSuitability.UsableWithCaution
            ? "Prepare with caution"
            : hasAnyActiveEmbedding
                ? "Embedding needs refresh"
                : "Embedding not available";
        var preparationMessage = quality.Suitability == FaceReferenceSuitability.UsableWithCaution
            ? quality.Message + " PRISM can prepare a current embedding, but another complete, frontal appearance is preferred when available."
            : hasAnyActiveEmbedding
                ? "This appearance has face data from an older or incompatible model. Prepare a current embedding before trusting it for matching."
                : "This confirmed face does not yet have a current matching embedding. Prepare it before using it as a trusted reference.";

        return Result(
            hasAnyActiveEmbedding
                ? FaceReferenceReadinessCode.EmbeddingOutdated
                : FaceReferenceReadinessCode.EmbeddingMissing,
            false,
            true,
            false,
            quality.Suitability,
            preparationLabel,
            preparationMessage);
    }

    private static ReferenceQualityAssessment AssessReferenceQuality(
        MediaFace face,
        FaceQualitySignals? signals,
        double minimumTrustedReferenceQuality)
    {
        if (face.QualityScore < minimumTrustedReferenceQuality)
        {
            return new ReferenceQualityAssessment(
                FaceReferenceSuitability.NotUsable,
                false,
                FaceReferenceReadinessCode.QualityTooLow,
                "Quality below reference threshold",
                $"Face quality {face.QualityScore:P0} is below the configured {minimumTrustedReferenceQuality:P0} trusted-reference threshold. Choose another appearance.");
        }

        if (face.QualityStatus is FaceQualityStatus.LowResolution
            or FaceQualityStatus.Blurred
            or FaceQualityStatus.PoorExposure
            or FaceQualityStatus.ExtremePose
            or FaceQualityStatus.SeverelyCropped
            or FaceQualityStatus.Suppressed)
        {
            var (label, message, code) = face.QualityStatus switch
            {
                FaceQualityStatus.LowResolution => (
                    "Face resolution too low",
                    "The detected face is too small for reliable trusted-reference evidence. Choose a larger, clearer appearance.",
                    FaceReferenceReadinessCode.QualityNotEligible),
                FaceQualityStatus.Blurred => (
                    "Face too blurred",
                    "The detected face lacks sufficient detail for reliable trusted-reference evidence. Choose a sharper appearance.",
                    FaceReferenceReadinessCode.QualityNotEligible),
                FaceQualityStatus.PoorExposure => (
                    "Exposure unsuitable",
                    "The detected face is too dark or too bright for reliable trusted-reference evidence. Choose a better exposed appearance.",
                    FaceReferenceReadinessCode.QualityNotEligible),
                FaceQualityStatus.ExtremePose => (
                    "Face pose unsuitable",
                    "The detected face angle is too oblique for reliable trusted-reference evidence. Choose a more frontal appearance.",
                    FaceReferenceReadinessCode.QualityNotEligible),
                FaceQualityStatus.SeverelyCropped => (
                    "Face crop too incomplete",
                    "Too much of the detected face lies at the photograph boundary for reliable matching evidence. Choose another appearance.",
                    FaceReferenceReadinessCode.CropTooIncomplete),
                _ => (
                    "Face not usable",
                    "This appearance cannot be used as trusted matching evidence.",
                    FaceReferenceReadinessCode.QualityNotEligible)
            };
            return new ReferenceQualityAssessment(
                FaceReferenceSuitability.NotUsable,
                false,
                code,
                label,
                message);
        }

        if (face.QualityStatus == FaceQualityStatus.ProcessingFailed)
        {
            return new ReferenceQualityAssessment(
                FaceReferenceSuitability.NotUsable,
                true,
                FaceReferenceReadinessCode.PreparationFailed,
                "Needs reprocessing",
                "The previous face-processing result was incomplete. Reprocess this appearance before deciding whether to trust it.");
        }

        if (face.QualityStatus == FaceQualityStatus.Occluded)
        {
            // Historical rows used Occluded for a crop-boundary measurement; no actual
            // occlusion detector existed. Use the persisted crop signal when available.
            if (signals?.CropCompleteness is { } legacyCrop && legacyCrop < 0.15d)
            {
                return new ReferenceQualityAssessment(
                    FaceReferenceSuitability.NotUsable,
                    false,
                    FaceReferenceReadinessCode.CropTooIncomplete,
                    "Face crop too incomplete",
                    "This legacy appearance is very close to the photograph boundary. Re-detect or choose another appearance.");
            }

            return new ReferenceQualityAssessment(
                FaceReferenceSuitability.UsableWithCaution,
                true,
                FaceReferenceReadinessCode.EligibleWithCaution,
                "Crop incomplete",
                "This legacy quality state represents crop-boundary completeness, not detected real-world occlusion. PRISM may prepare an embedding, but another complete face is preferred.");
        }

        if (face.QualityStatus == FaceQualityStatus.CropIncomplete)
        {
            return new ReferenceQualityAssessment(
                FaceReferenceSuitability.UsableWithCaution,
                true,
                FaceReferenceReadinessCode.EligibleWithCaution,
                "Crop incomplete",
                "The detected face is close to the photograph boundary. It may be used only after explicit reviewer trust; another complete face is preferred.");
        }

        if (face.QualityStatus == FaceQualityStatus.Detected)
        {
            return new ReferenceQualityAssessment(
                FaceReferenceSuitability.UsableWithCaution,
                true,
                FaceReferenceReadinessCode.EligibleWithCaution,
                "Usable with caution",
                "The face is technically usable for embedding but falls outside the preferred quality state. Explicit reviewer trust is required.");
        }

        if (face.QualityStatus != FaceQualityStatus.EmbeddingEligible)
        {
            return new ReferenceQualityAssessment(
                FaceReferenceSuitability.NotUsable,
                false,
                FaceReferenceReadinessCode.QualityNotEligible,
                "Face not usable",
                $"This appearance has quality state {Humanize(face.QualityStatus)} and cannot currently become a trusted reference.");
        }

        var cautions = new List<string>();
        if (signals is not null)
        {
            if (signals.Sharpness < 0.35d) cautions.Add("detail is below the preferred sharpness level");
            if (signals.Exposure < 0.35d) cautions.Add("exposure is outside the preferred range");
            if (signals.Contrast < 0.25d) cautions.Add("tonal contrast is below the preferred level");
            if (signals.Pose < 0.35d) cautions.Add("face pose is less frontal than preferred");
            if (signals.CropCompleteness < 0.65d) cautions.Add("the detected face crop is close to the photograph boundary");
        }

        return cautions.Count == 0
            ? new ReferenceQualityAssessment(
                FaceReferenceSuitability.Preferred,
                true,
                FaceReferenceReadinessCode.Eligible,
                "Preferred reference",
                "This appearance meets the preferred face-quality criteria for trusted matching evidence.")
            : new ReferenceQualityAssessment(
                FaceReferenceSuitability.UsableWithCaution,
                true,
                FaceReferenceReadinessCode.EligibleWithCaution,
                "Usable with caution",
                "This face has a valid embedding but " + string.Join(", ", cautions) + ". Explicit reviewer trust is required.");
    }

    private static FaceQualitySignals? ParseSignals(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FaceQualitySignals>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ReferenceQualityAssessment(
        FaceReferenceSuitability Suitability,
        bool CanReprocess,
        FaceReferenceReadinessCode Code,
        string Label,
        string Message);

    private static FaceReferenceReadiness Missing(Guid faceId)
        => new(
            faceId,
            0,
            FaceReferenceReadinessCode.AssignmentUnavailable,
            false,
            false,
            false,
            false,
            FaceReferenceSuitability.NotUsable,
            "Appearance unavailable",
            "This appearance is no longer actively assigned to this person.");

    private static string Humanize(FaceQualityStatus status)
        => status switch
        {
            FaceQualityStatus.LowResolution => "low resolution",
            FaceQualityStatus.Blurred => "blurred",
            FaceQualityStatus.PoorExposure => "poor exposure",
            FaceQualityStatus.ExtremePose => "extreme pose",
            FaceQualityStatus.Occluded => "legacy crop-incomplete",
            FaceQualityStatus.CropIncomplete => "crop incomplete",
            FaceQualityStatus.SeverelyCropped => "severely cropped",
            FaceQualityStatus.ProcessingFailed => "processing failed",
            FaceQualityStatus.EmbeddingEligible => "embedding eligible",
            FaceQualityStatus.Detected => "detected",
            FaceQualityStatus.Suppressed => "suppressed",
            _ => status.ToString()
        };

}
