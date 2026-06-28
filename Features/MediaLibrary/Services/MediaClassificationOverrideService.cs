using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public interface IMediaClassificationOverrideService
{
    Task SetManualAsync(long assetId, MediaClassification classification, string userId, string? reason, CancellationToken cancellationToken);
    Task<int> SetManualBatchAsync(IReadOnlyCollection<long> assetIds, MediaClassification classification, string userId, string? reason, CancellationToken cancellationToken);
    Task ResetToAutomaticAsync(long assetId, string userId, string? reason, CancellationToken cancellationToken);
}

public sealed class MediaClassificationOverrideService : IMediaClassificationOverrideService
{
    private readonly MediaLibraryDbContext _db;
    public MediaClassificationOverrideService(MediaLibraryDbContext db) => _db = db;

    public async Task SetManualAsync(long assetId, MediaClassification classification, string userId, string? reason, CancellationToken cancellationToken)
    {
        // --- Validate manual review input ---
        if (classification == MediaClassification.Unknown) throw new ArgumentException("Choose a specific classification.", nameof(classification));

        // --- Preserve automatic evidence while applying authoritative manual decision ---
        var asset = await _db.Assets.SingleAsync(x => x.Id == assetId, cancellationToken);
        var previous = asset.Classification;
        var previousManual = asset.ClassificationIsManual;
        var previousStatus = asset.ClassificationDecisionStatus;
        var now = DateTimeOffset.UtcNow;

        asset.Classification = classification;
        asset.ClassificationConfidence = 1;
        asset.ClassificationIsManual = true;
        asset.ClassificationUpdatedByUserId = userId;
        asset.ClassificationReviewedByUserId = userId;
        asset.ClassifiedAtUtc = now;
        asset.ClassificationReviewedAt = now;
        asset.ClassificationReviewReason = NormalizeReason(reason);
        asset.ClassificationDecisionStatus = classification == asset.PredictedClassification
            ? MediaClassificationDecisionStatus.ManuallyConfirmed
            : MediaClassificationDecisionStatus.ManuallyCorrected;
        asset.ClassificationDecisionReasonCode = classification == asset.PredictedClassification
            ? "MANUAL_CONFIRMATION"
            : "MANUAL_CORRECTION";
        asset.AnalysisStatus = MediaProcessingStatus.Ready;
        asset.ClassificationConcurrencyToken = Guid.NewGuid();

        _db.ClassificationAudits.Add(CreateAudit(assetId, previous, classification, previousManual, true, previousStatus, asset.ClassificationDecisionStatus, userId, reason, asset));
        await _db.SaveChangesAsync(cancellationToken);
    }


    public async Task<int> SetManualBatchAsync(
        IReadOnlyCollection<long> assetIds,
        MediaClassification classification,
        string userId,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        if (classification == MediaClassification.Unknown)
            throw new ArgumentException("Choose a specific classification.", nameof(classification));

        var distinctIds = assetIds.Where(id => id > 0).Distinct().Take(500).ToArray();
        if (distinctIds.Length == 0)
            return 0;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var assets = await _db.Assets
            .Where(asset => distinctIds.Contains(asset.Id)
                            && asset.IsAvailable
                            && !asset.IsDeleted
                            && !asset.IsArchived
                            && asset.Kind == MediaAssetKind.Photo)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        foreach (var asset in assets)
        {
            // --- Preserve automatic evidence while applying authoritative manual decision ---
            var previous = asset.Classification;
            var previousManual = asset.ClassificationIsManual;
            var previousStatus = asset.ClassificationDecisionStatus;
            asset.Classification = classification;
            asset.ClassificationConfidence = 1;
            asset.ClassificationIsManual = true;
            asset.ClassificationUpdatedByUserId = userId;
            asset.ClassificationReviewedByUserId = userId;
            asset.ClassifiedAtUtc = now;
            asset.ClassificationReviewedAt = now;
            asset.ClassificationReviewReason = normalizedReason;
            asset.ClassificationDecisionStatus = classification == asset.PredictedClassification
                ? MediaClassificationDecisionStatus.ManuallyConfirmed
                : MediaClassificationDecisionStatus.ManuallyCorrected;
            asset.ClassificationDecisionReasonCode = classification == asset.PredictedClassification
                ? "MANUAL_CONFIRMATION"
                : "MANUAL_CORRECTION";
            asset.AnalysisStatus = MediaProcessingStatus.Ready;
            asset.ClassificationConcurrencyToken = Guid.NewGuid();
            _db.ClassificationAudits.Add(CreateAudit(
                asset.Id,
                previous,
                classification,
                previousManual,
                true,
                previousStatus,
                asset.ClassificationDecisionStatus,
                userId,
                normalizedReason,
                asset));
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return assets.Count;
    }

    public async Task ResetToAutomaticAsync(long assetId, string userId, string? reason, CancellationToken cancellationToken)
    {
        var asset = await _db.Assets.SingleAsync(x => x.Id == assetId, cancellationToken);
        var previous = asset.Classification;
        var previousManual = asset.ClassificationIsManual;
        var previousStatus = asset.ClassificationDecisionStatus;
        asset.ClassificationIsManual = false;
        asset.ClassificationUpdatedByUserId = null;
        asset.ClassificationReviewedByUserId = null;
        asset.ClassificationReviewedAt = null;
        asset.ClassificationReviewReason = null;
        asset.ClassificationConfidence = null;
        asset.ClassifiedAtUtc = null;
        asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.NotProcessed;
        asset.ClassificationDecisionReasonCode = null;
        asset.ClassificationConcurrencyToken = Guid.NewGuid();
        asset.AnalysisStatus = MediaProcessingStatus.Pending;
        _db.ClassificationAudits.Add(CreateAudit(assetId, previous, previous, previousManual, false, previousStatus, asset.ClassificationDecisionStatus, userId, reason, asset));

        var job = await _db.ProcessingJobs.SingleOrDefaultAsync(x => x.MediaAssetId == assetId && x.JobType == MediaProcessingJobType.ClassifyMedia, cancellationToken);
        if (job is null)
        {
            _db.ProcessingJobs.Add(new MediaProcessingJob
            {
                MediaAssetId = assetId,
                JobType = MediaProcessingJobType.ClassifyMedia,
                Status = MediaProcessingJobStatus.Pending,
                AvailableAfterUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            job.Status = MediaProcessingJobStatus.Pending;
            job.AttemptCount = 0;
            job.AvailableAfterUtc = DateTimeOffset.UtcNow;
            job.FailureCode = null;
            job.FailureMessage = null;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static MediaClassificationAudit CreateAudit(long assetId, MediaClassification previous, MediaClassification next,
        bool previousManual, bool nextManual, MediaClassificationDecisionStatus previousStatus,
        MediaClassificationDecisionStatus nextStatus, string userId, string? reason, MediaAsset asset) => new()
    {
        MediaAssetId = assetId,
        PreviousClassification = previous,
        NewClassification = next,
        PreviousWasManual = previousManual,
        NewIsManual = nextManual,
        AutomaticPredictedClassification = asset.PredictedClassification,
        AutomaticPredictedScore = asset.PredictedClassificationScore,
        PreviousDecisionStatus = previousStatus,
        NewDecisionStatus = nextStatus,
        ChangedByUserId = userId,
        Reason = NormalizeReason(reason),
        ChangedAtUtc = DateTimeOffset.UtcNow
    };

    private static string? NormalizeReason(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}
