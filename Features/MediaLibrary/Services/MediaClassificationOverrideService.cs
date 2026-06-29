using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record ClassificationBatchItem(long AssetId, Guid ExpectedConcurrencyToken);

public interface IMediaClassificationOverrideService
{
    Task SetManualAsync(
        long assetId,
        Guid expectedConcurrencyToken,
        MediaClassification classification,
        string userId,
        string? reason,
        CancellationToken cancellationToken);

    Task<int> SetManualBatchAsync(
        IReadOnlyCollection<ClassificationBatchItem> items,
        MediaClassification classification,
        string userId,
        string reason,
        CancellationToken cancellationToken);

    Task ResetToAutomaticAsync(
        long assetId,
        Guid expectedConcurrencyToken,
        string userId,
        string? reason,
        CancellationToken cancellationToken);
}

public sealed class MediaClassificationConcurrencyException : InvalidOperationException
{
    public MediaClassificationConcurrencyException(string message) : base(message) { }
    public MediaClassificationConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class MediaClassificationOverrideService : IMediaClassificationOverrideService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaContentChangeInvalidationService _contentInvalidation;

    public MediaClassificationOverrideService(
        MediaLibraryDbContext db,
        IMediaContentChangeInvalidationService contentInvalidation)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _contentInvalidation = contentInvalidation ?? throw new ArgumentNullException(nameof(contentInvalidation));
    }

    public async Task SetManualAsync(
        long assetId,
        Guid expectedConcurrencyToken,
        MediaClassification classification,
        string userId,
        string? reason,
        CancellationToken cancellationToken)
    {
        ValidateClassification(classification);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var asset = await LoadReviewableAssetAsync(assetId, cancellationToken);
        EnsureToken(asset, expectedConcurrencyToken);
        if (classification != asset.PredictedClassification && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A reason is required when correcting the automatic prediction.",
                nameof(reason));
        }

        var now = DateTimeOffset.UtcNow;
        ApplyManual(asset, classification, userId, reason, now);
        if (classification != MediaClassification.Photograph)
        {
            await RetireFaceIntelligenceAsync(
                new[] { asset },
                userId,
                reason,
                now,
                cancellationToken);
        }

        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> SetManualBatchAsync(
        IReadOnlyCollection<ClassificationBatchItem> items,
        MediaClassification classification,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ValidateClassification(classification);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required for bulk classification.", nameof(reason));
        }
        if (items.Count == 0) return 0;
        if (items.Count > 500)
        {
            throw new ArgumentException("A maximum of 500 images may be updated at once.", nameof(items));
        }

        var map = items
            .GroupBy(item => item.AssetId)
            .ToDictionary(group => group.Key, group => group.Last().ExpectedConcurrencyToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var assets = await ReviewableAssets()
            .Where(asset => map.Keys.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (assets.Count != map.Count)
        {
            throw new InvalidOperationException(
                "One or more selected images are no longer available. No changes were applied.");
        }

        foreach (var asset in assets)
        {
            EnsureToken(asset, map[asset.Id]);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var asset in assets)
        {
            ApplyManual(asset, classification, userId, reason, now);
        }
        if (classification != MediaClassification.Photograph)
        {
            await RetireFaceIntelligenceAsync(
                assets,
                userId,
                reason,
                now,
                cancellationToken);
        }

        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return assets.Count;
    }

    public async Task ResetToAutomaticAsync(
        long assetId,
        Guid expectedConcurrencyToken,
        string userId,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var asset = await LoadReviewableAssetAsync(assetId, cancellationToken);
        EnsureToken(asset, expectedConcurrencyToken);
        var previous = asset.Classification;
        var previousManual = asset.ClassificationIsManual;
        var previousStatus = asset.ClassificationDecisionStatus;
        var previousPrediction = asset.PredictedClassification;
        var previousPredictionScore = asset.PredictedClassificationScore;
        var now = DateTimeOffset.UtcNow;

        asset.Classification = MediaClassification.Unknown;
        asset.PredictedClassification = MediaClassification.Unknown;
        asset.PredictedClassificationScore = 0;
        asset.ClassificationConfidence = null;
        asset.ClassificationIsManual = false;
        asset.ClassificationUpdatedByUserId = null;
        asset.ClassificationReviewedByUserId = null;
        asset.ClassificationReviewedAt = null;
        asset.ClassificationReviewReason = null;
        asset.ClassifiedAtUtc = null;
        asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.NotProcessed;
        asset.ClassificationDecisionReasonCode = "MANUAL_RESET";
        asset.ClassifierVersion = null;
        asset.AnalysisVersion = null;
        asset.AnalysisSignalsJson = null;
        asset.AutomaticClassificationSignalsJson = null;
        asset.AutomaticClassificationScoresJson = null;
        asset.AutomaticClassificationMetricsJson = null;
        asset.AnalysedAtUtc = null;
        asset.AnalysisStatus = MediaProcessingStatus.Pending;
        asset.ClassificationConcurrencyToken = Guid.NewGuid();

        await RetireFaceIntelligenceAsync(
            new[] { asset },
            userId,
            reason,
            now,
            cancellationToken);

        _db.ClassificationAudits.Add(CreateAudit(
            asset,
            previous,
            MediaClassification.Unknown,
            previousManual,
            false,
            previousStatus,
            asset.ClassificationDecisionStatus,
            previousPrediction,
            previousPredictionScore,
            userId,
            reason));

        var activeClassificationJob = await _db.ProcessingJobs.AnyAsync(
            item => item.MediaAssetId == assetId
                    && item.Status == MediaProcessingJobStatus.Running
                    && item.LockExpiresAtUtc != null
                    && item.LockExpiresAtUtc > now
                    && (item.JobType == MediaProcessingJobType.AnalyseAsset
                        || item.JobType == MediaProcessingJobType.ReclassifyAsset
                        || item.JobType == MediaProcessingJobType.ClassifyMedia
                        || item.JobType == MediaProcessingJobType.RebuildIntelligence),
            cancellationToken);
        if (!activeClassificationJob)
        {
            var job = await _db.ProcessingJobs.SingleOrDefaultAsync(
                item => item.MediaAssetId == assetId
                        && item.JobType == MediaProcessingJobType.ClassifyMedia,
                cancellationToken);
            if (job is null)
            {
                _db.ProcessingJobs.Add(new MediaProcessingJob
                {
                    MediaAssetId = assetId,
                    JobType = MediaProcessingJobType.ClassifyMedia,
                    Status = MediaProcessingJobStatus.Pending,
                    AvailableAfterUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            else
            {
                job.Status = MediaProcessingJobStatus.Pending;
                job.AttemptCount = 0;
                job.AvailableAfterUtc = now;
                job.StartedAtUtc = null;
                job.CompletedAtUtc = null;
                job.LockedBy = null;
                job.LockExpiresAtUtc = null;
                job.FailureCode = null;
                job.FailureMessage = null;
                job.UpdatedAtUtc = now;
            }
        }

        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<MediaAsset> ReviewableAssets()
        => _db.Assets.Where(asset => asset.IsAvailable
                                     && !asset.IsDeleted
                                     && !asset.IsArchived
                                     && asset.Kind == MediaAssetKind.Photo);

    private async Task<MediaAsset> LoadReviewableAssetAsync(
        long assetId,
        CancellationToken cancellationToken)
        => await ReviewableAssets().SingleOrDefaultAsync(asset => asset.Id == assetId, cancellationToken)
           ?? throw new InvalidOperationException(
               "This image is no longer available in the classification review queue.");

    private void ApplyManual(
        MediaAsset asset,
        MediaClassification classification,
        string userId,
        string? reason,
        DateTimeOffset now)
    {
        var previous = asset.Classification;
        var previousManual = asset.ClassificationIsManual;
        var previousStatus = asset.ClassificationDecisionStatus;
        var confirmsPrediction = classification == asset.PredictedClassification;

        asset.Classification = classification;
        asset.ClassificationConfidence = 1;
        asset.ClassificationIsManual = true;
        asset.ClassificationUpdatedByUserId = userId;
        asset.ClassificationReviewedByUserId = userId;
        asset.ClassifiedAtUtc = now;
        asset.ClassificationReviewedAt = now;
        asset.ClassificationReviewReason = Normalize(reason);
        asset.ClassificationDecisionStatus = confirmsPrediction
            ? MediaClassificationDecisionStatus.ManuallyConfirmed
            : MediaClassificationDecisionStatus.ManuallyCorrected;
        asset.ClassificationDecisionReasonCode = confirmsPrediction
            ? "MANUAL_CONFIRMATION"
            : "MANUAL_CORRECTION";
        asset.AnalysisStatus = MediaProcessingStatus.Ready;
        asset.ClassificationConcurrencyToken = Guid.NewGuid();

        _db.ClassificationAudits.Add(CreateAudit(
            asset,
            previous,
            classification,
            previousManual,
            true,
            previousStatus,
            asset.ClassificationDecisionStatus,
            asset.PredictedClassification,
            asset.PredictedClassificationScore,
            userId,
            reason));
    }

    private async Task RetireFaceIntelligenceAsync(
        IReadOnlyCollection<MediaAsset> assets,
        string userId,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ids = assets.Select(asset => asset.Id).Distinct().ToArray();
        await _contentInvalidation.RetireFaceIntelligenceAsync(
            ids,
            "ClassificationRevoked",
            userId,
            string.IsNullOrWhiteSpace(reason)
                ? "Face intelligence retired because a human reviewer classified the image as non-photographic."
                : $"Face intelligence retired after human classification review: {reason.Trim()}",
            now,
            cancellationToken);

        foreach (var asset in assets)
        {
            asset.FaceAnalysisStatus = MediaProcessingStatus.NotRequested;
            asset.FaceAnalysisVersion = null;
            asset.FaceAnalysedAtUtc = null;
            asset.FaceProcessingFailureReason = null;
        }

        await _db.ProcessingJobs
            .Where(job => ids.Contains(job.MediaAssetId)
                          && (job.JobType == MediaProcessingJobType.DetectFaces
                              || job.JobType == MediaProcessingJobType.GenerateFaceEmbeddings
                              || job.JobType == MediaProcessingJobType.AssignFaceCluster)
                          && job.Status != MediaProcessingJobStatus.Running
                          && job.Status != MediaProcessingJobStatus.Completed)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static void ValidateClassification(MediaClassification classification)
    {
        if (classification == MediaClassification.Unknown
            || !Enum.IsDefined(classification))
        {
            throw new ArgumentException("Choose a specific classification.", nameof(classification));
        }
    }

    private static void EnsureToken(MediaAsset asset, Guid expected)
    {
        if (expected == Guid.Empty || asset.ClassificationConcurrencyToken != expected)
        {
            throw new MediaClassificationConcurrencyException(
                "This image was changed by another reviewer. Reload the page and review the latest result.");
        }
    }

    private static MediaClassificationAudit CreateAudit(
        MediaAsset asset,
        MediaClassification previous,
        MediaClassification next,
        bool previousManual,
        bool nextManual,
        MediaClassificationDecisionStatus previousStatus,
        MediaClassificationDecisionStatus nextStatus,
        MediaClassification automaticPrediction,
        decimal automaticScore,
        string userId,
        string? reason)
        => new()
        {
            MediaAssetId = asset.Id,
            PreviousClassification = previous,
            NewClassification = next,
            PreviousWasManual = previousManual,
            NewIsManual = nextManual,
            AutomaticPredictedClassification = automaticPrediction,
            AutomaticPredictedScore = automaticScore,
            PreviousDecisionStatus = previousStatus,
            NewDecisionStatus = nextStatus,
            ChangedByUserId = userId,
            Reason = Normalize(reason),
            CorrelationId = Guid.NewGuid().ToString("N"),
            ChangedAtUtc = DateTimeOffset.UtcNow
        };

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new MediaClassificationConcurrencyException(
                "This image was changed by another reviewer. Reload the page and review the latest result.",
                exception);
        }
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 1024 ? normalized : normalized[..1024];
    }
}
