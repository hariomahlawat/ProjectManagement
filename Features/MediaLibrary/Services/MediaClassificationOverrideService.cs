using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record ClassificationBatchItem(long AssetId, Guid ExpectedConcurrencyToken);

public interface IMediaClassificationOverrideService
{
    Task SetManualAsync(long assetId, Guid expectedConcurrencyToken, MediaClassification classification, string userId, string? reason, CancellationToken cancellationToken);
    Task<int> SetManualBatchAsync(IReadOnlyCollection<ClassificationBatchItem> items, MediaClassification classification, string userId, string reason, CancellationToken cancellationToken);
    Task ResetToAutomaticAsync(long assetId, Guid expectedConcurrencyToken, string userId, string? reason, CancellationToken cancellationToken);
}

public sealed class MediaClassificationConcurrencyException : InvalidOperationException
{
    public MediaClassificationConcurrencyException(string message) : base(message) { }
}

public sealed class MediaClassificationOverrideService : IMediaClassificationOverrideService
{
    private readonly MediaLibraryDbContext _db;
    public MediaClassificationOverrideService(MediaLibraryDbContext db) => _db = db;

    public async Task SetManualAsync(long assetId, Guid expectedConcurrencyToken, MediaClassification classification, string userId, string? reason, CancellationToken ct)
    {
        if (classification == MediaClassification.Unknown) throw new ArgumentException("Choose a specific classification.", nameof(classification));
        var asset = await _db.Assets.SingleAsync(x => x.Id == assetId, ct);
        EnsureToken(asset, expectedConcurrencyToken);
        ApplyManual(asset, classification, userId, reason, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> SetManualBatchAsync(IReadOnlyCollection<ClassificationBatchItem> items, MediaClassification classification, string userId, string reason, CancellationToken ct)
    {
        if (classification == MediaClassification.Unknown) throw new ArgumentException("Choose a specific classification.", nameof(classification));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required for bulk classification.", nameof(reason));
        if (items.Count == 0) return 0;
        if (items.Count > 500) throw new ArgumentException("A maximum of 500 images may be updated at once.", nameof(items));
        var map=items.GroupBy(x=>x.AssetId).ToDictionary(x=>x.Key,x=>x.Last().ExpectedConcurrencyToken);
        await using var tx=await _db.Database.BeginTransactionAsync(ct);
        var assets=await _db.Assets.Where(x=>map.Keys.Contains(x.Id) && x.IsAvailable && !x.IsDeleted && !x.IsArchived && x.Kind==MediaAssetKind.Photo).ToListAsync(ct);
        if(assets.Count!=map.Count) throw new InvalidOperationException("One or more selected images are no longer available. No changes were applied.");
        foreach(var asset in assets) EnsureToken(asset,map[asset.Id]);
        var now=DateTimeOffset.UtcNow;
        foreach(var asset in assets) ApplyManual(asset,classification,userId,reason,now);
        await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return assets.Count;
    }

    public async Task ResetToAutomaticAsync(long assetId, Guid expectedConcurrencyToken, string userId, string? reason, CancellationToken ct)
    {
        var asset=await _db.Assets.SingleAsync(x=>x.Id==assetId,ct); EnsureToken(asset,expectedConcurrencyToken);
        var previous=asset.Classification; var previousManual=asset.ClassificationIsManual; var previousStatus=asset.ClassificationDecisionStatus;
        asset.Classification=MediaClassification.Unknown; asset.PredictedClassification=MediaClassification.Unknown; asset.PredictedClassificationScore=0;
        asset.ClassificationConfidence=null; asset.ClassificationIsManual=false; asset.ClassificationUpdatedByUserId=null; asset.ClassificationReviewedByUserId=null;
        asset.ClassificationReviewedAt=null; asset.ClassificationReviewReason=null; asset.ClassifiedAtUtc=null; asset.ClassificationDecisionStatus=MediaClassificationDecisionStatus.NotProcessed;
        asset.ClassificationDecisionReasonCode=null; asset.ClassifierVersion=null; asset.AnalysisVersion=null; asset.AnalysisSignalsJson=null; asset.AutomaticClassificationSignalsJson=null;
        asset.AutomaticClassificationScoresJson=null; asset.AutomaticClassificationMetricsJson=null; asset.AnalysedAtUtc=null; asset.AnalysisStatus=MediaProcessingStatus.Pending; asset.ClassificationConcurrencyToken=Guid.NewGuid();
        _db.ClassificationAudits.Add(CreateAudit(asset,previous,MediaClassification.Unknown,previousManual,false,previousStatus,asset.ClassificationDecisionStatus,userId,reason));
        var job=await _db.ProcessingJobs.SingleOrDefaultAsync(x=>x.MediaAssetId==assetId && x.JobType==MediaProcessingJobType.ClassifyMedia,ct);
        if(job is null) _db.ProcessingJobs.Add(new MediaProcessingJob{MediaAssetId=assetId,JobType=MediaProcessingJobType.ClassifyMedia,Status=MediaProcessingJobStatus.Pending,AvailableAfterUtc=DateTimeOffset.UtcNow,CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow});
        else {job.Status=MediaProcessingJobStatus.Pending;job.AttemptCount=0;job.AvailableAfterUtc=DateTimeOffset.UtcNow;job.FailureCode=null;job.FailureMessage=null;job.UpdatedAtUtc=DateTimeOffset.UtcNow;}
        await _db.SaveChangesAsync(ct);
    }

    private void ApplyManual(MediaAsset asset, MediaClassification classification,string userId,string? reason,DateTimeOffset now)
    {
        var previous=asset.Classification;var previousManual=asset.ClassificationIsManual;var previousStatus=asset.ClassificationDecisionStatus;
        asset.Classification=classification;asset.ClassificationConfidence=1;asset.ClassificationIsManual=true;asset.ClassificationUpdatedByUserId=userId;asset.ClassificationReviewedByUserId=userId;
        asset.ClassifiedAtUtc=now;asset.ClassificationReviewedAt=now;asset.ClassificationReviewReason=Normalize(reason);asset.ClassificationDecisionStatus=classification==asset.PredictedClassification?MediaClassificationDecisionStatus.ManuallyConfirmed:MediaClassificationDecisionStatus.ManuallyCorrected;
        asset.ClassificationDecisionReasonCode=classification==asset.PredictedClassification?"MANUAL_CONFIRMATION":"MANUAL_CORRECTION";asset.AnalysisStatus=MediaProcessingStatus.Ready;asset.ClassificationConcurrencyToken=Guid.NewGuid();
        _db.ClassificationAudits.Add(CreateAudit(asset,previous,classification,previousManual,true,previousStatus,asset.ClassificationDecisionStatus,userId,reason));
    }
    private static void EnsureToken(MediaAsset asset,Guid expected){if(expected==Guid.Empty||asset.ClassificationConcurrencyToken!=expected) throw new MediaClassificationConcurrencyException("This image was changed by another reviewer. Reload the page and review the latest result.");}
    private static MediaClassificationAudit CreateAudit(MediaAsset asset,MediaClassification previous,MediaClassification next,bool previousManual,bool nextManual,MediaClassificationDecisionStatus previousStatus,MediaClassificationDecisionStatus nextStatus,string userId,string? reason)=>new(){MediaAssetId=asset.Id,PreviousClassification=previous,NewClassification=next,PreviousWasManual=previousManual,NewIsManual=nextManual,AutomaticPredictedClassification=asset.PredictedClassification,AutomaticPredictedScore=asset.PredictedClassificationScore,PreviousDecisionStatus=previousStatus,NewDecisionStatus=nextStatus,ChangedByUserId=userId,Reason=Normalize(reason),CorrelationId=Guid.NewGuid().ToString("N"),ChangedAtUtc=DateTimeOffset.UtcNow};
    private static string? Normalize(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
