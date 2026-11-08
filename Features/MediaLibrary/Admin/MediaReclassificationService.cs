using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Features.MediaLibrary.Admin;

/// <summary>
/// "Sort older images again" on Classification review: images PRISM sorted with an older
/// classifier version are reset and queued for the current one, a bounded batch at a time.
/// </summary>
public interface IMediaReclassificationService
{
    Task<MediaReclassifyStaleResult> QueueStaleAsync(string userId, CancellationToken cancellationToken);
}

public enum MediaReclassifyStaleOutcome
{
    /// <summary>Every image PRISM sorted already uses the current version.</summary>
    NothingStale,

    /// <summary>Every older image is already waiting in the classification queue.</summary>
    AllAlreadyWaiting,

    /// <summary>Every older image in this batch is being processed by a worker right now.</summary>
    AllBusy,

    /// <summary>Some images changed while this ran; nothing was changed.</summary>
    ChangedMeanwhile,

    Queued
}

/// <param name="Queued">Images reset and waiting to be sorted again.</param>
/// <param name="SkippedBusy">Images left alone because a worker was processing them.</param>
/// <param name="MoreRemain">More older images remain after this batch.</param>
public sealed record MediaReclassifyStaleResult(
    MediaReclassifyStaleOutcome Outcome,
    int Queued,
    int SkippedBusy,
    bool MoreRemain)
{
    public static MediaReclassifyStaleResult Of(MediaReclassifyStaleOutcome outcome) => new(outcome, 0, 0, false);
}

/// <remarks>
/// Fourth admin review, finding C2. The workflow used to live in the page and decided which
/// images were "already being processed" before its transaction, without a lock, then reset
/// every ClassifyMedia job it found to Pending with no lock holder. A job a worker claimed in
/// between was un-claimed, so a second worker could sort the same image at the same time.
/// Now the job rows are locked (FOR UPDATE) inside the transaction before anything is decided:
/// a worker claiming with FOR UPDATE SKIP LOCKED either finished its claim before the lock (and
/// the image is skipped as busy) or skips the locked row until this commits.
/// </remarks>
public sealed class MediaReclassificationService : IMediaReclassificationService
{
    /// <summary>One request queues at most this many images; run it again for more.</summary>
    public const int MaximumBatch = 500;

    public const string AdminAuditAction = "MediaClassificationsReclassifyStaleQueued";

    private static readonly MediaProcessingJobType[] FaceJobTypes =
    {
        MediaProcessingJobType.DetectFaces,
        MediaProcessingJobType.GenerateFaceEmbeddings,
        MediaProcessingJobType.AssignFaceCluster
    };

    // Jobs that classify the image, and so must not run while it is reset.
    private static readonly MediaProcessingJobType[] ClassifyingJobTypes =
    {
        MediaProcessingJobType.AnalyseAsset,
        MediaProcessingJobType.ReclassifyAsset,
        MediaProcessingJobType.ClassifyMedia,
        MediaProcessingJobType.RebuildIntelligence
    };

    /// <summary>The images Classification review works on.</summary>
    public static readonly Expression<Func<MediaAsset, bool>> IsReviewable = asset =>
        asset.IsAvailable
        && !asset.IsDeleted
        && !asset.IsArchived
        && asset.Kind == MediaAssetKind.Photo;

    /// <summary>Sorted automatically by an older classifier version (or reset and not yet sorted).</summary>
    public static readonly Expression<Func<MediaAsset, bool>> IsStale = asset =>
        !asset.ClassificationIsManual
        && asset.ClassifierVersion != MediaClassifier.ClassifierVersion;

    private readonly MediaLibraryDbContext _db;
    private readonly IMediaContentChangeInvalidationService _contentInvalidation;
    private readonly IAdminAuditService _adminAudit;
    private readonly IClock _clock;
    private readonly ILogger<MediaReclassificationService> _logger;

    public MediaReclassificationService(
        MediaLibraryDbContext db,
        IMediaContentChangeInvalidationService contentInvalidation,
        IAdminAuditService adminAudit,
        IClock clock,
        ILogger<MediaReclassificationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _contentInvalidation = contentInvalidation ?? throw new ArgumentNullException(nameof(contentInvalidation));
        _adminAudit = adminAudit ?? throw new ArgumentNullException(nameof(adminAudit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaReclassifyStaleResult> QueueStaleAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var now = _clock.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // A reset image keeps matching the stale predicate (its ClassifierVersion is null) until
        // the worker sorts it, so images already waiting in the classification queue are left
        // out; otherwise running this again would re-reset the same first batch.
        var staleCandidates = _db.Assets.Where(IsReviewable).Where(IsStale);
        var candidateIds = await staleCandidates
            .Where(asset => !_db.ProcessingJobs.Any(job =>
                job.MediaAssetId == asset.Id
                && job.JobType == MediaProcessingJobType.ClassifyMedia
                && job.Status == MediaProcessingJobStatus.Pending))
            .OrderBy(asset => asset.Id)
            .Select(asset => asset.Id)
            .Take(MaximumBatch + 1)
            .ToListAsync(cancellationToken);
        if (candidateIds.Count == 0)
        {
            return MediaReclassifyStaleResult.Of(await staleCandidates.AnyAsync(cancellationToken)
                ? MediaReclassifyStaleOutcome.AllAlreadyWaiting
                : MediaReclassifyStaleOutcome.NothingStale);
        }

        var moreRemain = candidateIds.Count > MaximumBatch;
        var batchIds = candidateIds.Take(MaximumBatch).ToArray();

        // From here on no worker can claim, finish or release these images' jobs until commit.
        await LockJobsAsync(batchIds, cancellationToken);
        var jobs = await _db.ProcessingJobs
            .Where(job => batchIds.Contains(job.MediaAssetId))
            .ToListAsync(cancellationToken);
        var busy = jobs
            .Where(job => ClassifyingJobTypes.Contains(job.JobType) && IsHeldByAWorker(job, now))
            .Select(job => job.MediaAssetId)
            .ToHashSet();
        // Queued by someone else since the candidates were read: already in the desired state.
        var nowWaiting = jobs
            .Where(job => job.JobType == MediaProcessingJobType.ClassifyMedia
                          && job.Status == MediaProcessingJobStatus.Pending)
            .Select(job => job.MediaAssetId)
            .ToHashSet();

        var queueableIds = batchIds.Where(id => !busy.Contains(id) && !nowWaiting.Contains(id)).ToArray();
        var assets = await _db.Assets
            .Where(IsReviewable)
            .Where(IsStale)
            .Where(asset => queueableIds.Contains(asset.Id))
            .OrderBy(asset => asset.Id)
            .ToListAsync(cancellationToken);
        var skippedBusy = busy.Count;
        if (assets.Count == 0)
        {
            return MediaReclassifyStaleResult.Of(skippedBusy > 0
                ? MediaReclassifyStaleOutcome.AllBusy
                : MediaReclassifyStaleOutcome.AllAlreadyWaiting);
        }

        var assetIds = assets.Select(asset => asset.Id).ToArray();

        // A classifier upgrade changes the admission policy that authorised any prior face
        // processing. Retire derived face intelligence before resetting the classification so
        // a v6 false positive can never survive the v7 safety-policy transition.
        await _contentInvalidation.RetireFaceIntelligenceAsync(
            assetIds,
            "ClassifierUpgrade",
            userId,
            $"Face intelligence retired before reclassification with {MediaClassifier.ClassifierVersion}.",
            now,
            cancellationToken);
        var queued = assetIds.ToHashSet();
        _db.ProcessingJobs.RemoveRange(jobs.Where(job =>
            queued.Contains(job.MediaAssetId)
            && FaceJobTypes.Contains(job.JobType)
            && job.Status != MediaProcessingJobStatus.Running
            && job.Status != MediaProcessingJobStatus.Completed));
        var classifyJobs = jobs
            .Where(job => queued.Contains(job.MediaAssetId) && job.JobType == MediaProcessingJobType.ClassifyMedia)
            .ToDictionary(job => job.MediaAssetId);

        foreach (var asset in assets)
        {
            _db.ClassificationAudits.Add(new MediaClassificationAudit
            {
                MediaAssetId = asset.Id,
                PreviousClassification = asset.Classification,
                NewClassification = MediaClassification.Unknown,
                PreviousWasManual = asset.ClassificationIsManual,
                NewIsManual = false,
                AutomaticPredictedClassification = asset.PredictedClassification,
                AutomaticPredictedScore = asset.PredictedClassificationScore,
                PreviousDecisionStatus = asset.ClassificationDecisionStatus,
                NewDecisionStatus = MediaClassificationDecisionStatus.NotProcessed,
                CorrelationId = $"reclassify:{Guid.NewGuid():N}",
                ChangedByUserId = userId,
                Reason = $"Queued for classifier upgrade to {MediaClassifier.ClassifierVersion}.",
                ChangedAtUtc = now
            });
            ResetClassification(asset);

            if (classifyJobs.TryGetValue(asset.Id, out var job))
            {
                // Not held by a worker: busy images were left out above, under the row lock.
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
            else
            {
                _db.ProcessingJobs.Add(new MediaProcessingJob
                {
                    MediaAssetId = asset.Id,
                    JobType = MediaProcessingJobType.ClassifyMedia,
                    Status = MediaProcessingJobStatus.Pending,
                    AvailableAfterUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    MaxAttempts = 5
                });
            }
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // An image's classification changed between reading and saving (a reviewer, the
            // catalogue scanner, or a worker whose lease had run out). Nothing is saved.
            _logger.LogWarning(exception, "Sort older images again found a changed image; nothing was queued.");
            _db.ChangeTracker.Clear();
            return MediaReclassifyStaleResult.Of(MediaReclassifyStaleOutcome.ChangedMeanwhile);
        }

        // Per-image MediaClassificationAudit rows commit with the change above. This admin
        // audit row summarises the operation in the admin log; it is written after the media
        // commit (separate database context), best effort.
        await AuditBestEffortAsync(assets.Count, skippedBusy, moreRemain, userId, cancellationToken);

        return new MediaReclassifyStaleResult(MediaReclassifyStaleOutcome.Queued, assets.Count, skippedBusy, moreRemain);
    }

    private static bool IsHeldByAWorker(MediaProcessingJob job, DateTimeOffset now)
        // An expired lease is recovered by the worker itself (RecoverExpiredLocksAsync), so it
        // does not count as held.
        => job.Status == MediaProcessingJobStatus.Running
           && job.LockExpiresAtUtc != null
           && job.LockExpiresAtUtc > now;

    private async Task LockJobsAsync(long[] assetIds, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsNpgsql())
        {
            return;
        }

        // Ordered by Id so two overlapping requests take the locks in the same order.
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1
            FROM "MediaProcessingJobs"
            WHERE "MediaAssetId" = ANY({assetIds})
            ORDER BY "Id"
            FOR UPDATE
            """, cancellationToken);
    }

    private static void ResetClassification(MediaAsset asset)
    {
        asset.AnalysisStatus = MediaProcessingStatus.Pending;
        asset.PredictedClassification = MediaClassification.Unknown;
        asset.PredictedClassificationScore = 0m;
        asset.Classification = MediaClassification.Unknown;
        asset.ClassificationConfidence = null;
        asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.NotProcessed;
        asset.ClassificationDecisionReasonCode = "CLASSIFIER_VERSION_CHANGED";
        asset.AutomaticClassificationSignalsJson = null;
        asset.AutomaticClassificationScoresJson = null;
        asset.AutomaticClassificationMetricsJson = null;
        asset.ClassificationConcurrencyToken = Guid.NewGuid();
        asset.FaceAnalysisStatus = MediaProcessingStatus.NotRequested;
        asset.FaceAnalysisVersion = null;
        asset.FaceAnalysedAtUtc = null;
        asset.FaceProcessingFailureReason = null;
        asset.AnalysisVersion = null;
        asset.ClassifierVersion = null;
        asset.AnalysisSignalsJson = null;
        asset.AnalysedAtUtc = null;
        asset.ClassifiedAtUtc = null;
    }

    private async Task AuditBestEffortAsync(
        int queued,
        int skipped,
        bool moreRemain,
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _adminAudit.RecordAsync(
                new AdminAuditEntry(
                    Action: AdminAuditAction,
                    EntityType: "MediaAsset",
                    After: new
                    {
                        Queued = queued,
                        SkippedActive = skipped,
                        MoreRemain = moreRemain,
                        ClassifierVersion = MediaClassifier.ClassifierVersion,
                        RequestedByUserId = userId
                    },
                    Origin: "Admin.MediaIntelligence.Classifications",
                    Level: "Warning",
                    Message: $"Queued {queued} stale image(s) for reclassification with {MediaClassifier.ClassifierVersion}; their face intelligence was retired."),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "Reclassify stale queued {Queued} image(s) but its admin audit event could not be written.",
                queued);
        }
    }
}
