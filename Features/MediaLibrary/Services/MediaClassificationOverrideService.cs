using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public interface IMediaClassificationOverrideService
{
    Task SetManualAsync(long assetId, MediaClassification classification, string userId, string? reason, CancellationToken cancellationToken);
    Task ResetToAutomaticAsync(long assetId, string userId, string? reason, CancellationToken cancellationToken);
}

public sealed class MediaClassificationOverrideService : IMediaClassificationOverrideService
{
    private readonly MediaLibraryDbContext _db;
    public MediaClassificationOverrideService(MediaLibraryDbContext db) => _db = db;

    public async Task SetManualAsync(long assetId, MediaClassification classification, string userId, string? reason, CancellationToken cancellationToken)
    {
        if (classification == MediaClassification.Unknown) throw new ArgumentException("Choose a specific classification.", nameof(classification));
        var asset = await _db.Assets.SingleAsync(x => x.Id == assetId, cancellationToken);
        var previous = asset.Classification;
        var previousManual = asset.ClassificationIsManual;
        asset.Classification = classification;
        asset.ClassificationConfidence = 1;
        asset.ClassificationIsManual = true;
        asset.ClassificationUpdatedByUserId = userId;
        asset.ClassifiedAtUtc = DateTimeOffset.UtcNow;
        asset.ClassifierVersion = "manual";
        asset.AnalysisVersion = "manual";
        asset.AnalysisStatus = MediaProcessingStatus.Ready;
        asset.AnalysisSignalsJson = "[\"Manual classification confirmed by an authorised user.\"]";
        _db.ClassificationAudits.Add(CreateAudit(assetId, previous, classification, previousManual, true, userId, reason));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetToAutomaticAsync(long assetId, string userId, string? reason, CancellationToken cancellationToken)
    {
        var asset = await _db.Assets.SingleAsync(x => x.Id == assetId, cancellationToken);
        var previous = asset.Classification;
        var previousManual = asset.ClassificationIsManual;
        asset.ClassificationIsManual = false;
        asset.ClassificationUpdatedByUserId = null;
        asset.ClassificationConfidence = null;
        asset.ClassifierVersion = null;
        asset.ClassifiedAtUtc = null;
        asset.AnalysisStatus = MediaProcessingStatus.Pending;
        _db.ClassificationAudits.Add(CreateAudit(assetId, previous, previous, previousManual, false, userId, reason));

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
        bool previousManual, bool nextManual, string userId, string? reason) => new()
    {
        MediaAssetId = assetId,
        PreviousClassification = previous,
        NewClassification = next,
        PreviousWasManual = previousManual,
        NewIsManual = nextManual,
        ChangedByUserId = userId,
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        ChangedAtUtc = DateTimeOffset.UtcNow
    };
}
