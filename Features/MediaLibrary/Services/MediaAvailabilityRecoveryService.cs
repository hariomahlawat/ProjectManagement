using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaAvailabilityRecoveryResult(
    int Examined,
    int Restored,
    int StillUnavailable,
    int Errors,
    bool HasMore);

public sealed record MediaAvailabilityReconciliationResult(
    int Examined,
    int Restored,
    int MarkedUnavailable,
    int Errors,
    bool HasMore);

public interface IMediaAvailabilityRecoveryService
{
    Task<MediaAvailabilityRecoveryResult> RecheckAsync(long? assetId, int batchSize, CancellationToken cancellationToken);
    Task<MediaAvailabilityReconciliationResult> ReconcileHistoricalAsync(int batchSize, CancellationToken cancellationToken);
}

public sealed class MediaAvailabilityRecoveryService : IMediaAvailabilityRecoveryService
{
    private const int MaximumBatchSize = 250;
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaContentProviderResolver _contentResolver;
    private readonly ILogger<MediaAvailabilityRecoveryService> _logger;

    public MediaAvailabilityRecoveryService(
        MediaLibraryDbContext db,
        IMediaContentProviderResolver contentResolver,
        ILogger<MediaAvailabilityRecoveryService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaAvailabilityRecoveryResult> RecheckAsync(
        long? assetId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(batchSize, 1, MaximumBatchSize);
        var query = _db.Assets
            .Include(asset => asset.Source)
            .Where(asset => !asset.IsDeleted
                            && (!asset.IsAvailable || asset.AvailabilityStatus != MediaAvailabilityStatus.Available));
        if (assetId.HasValue) query = query.Where(asset => asset.Id == assetId.Value);

        var assets = await query.OrderBy(asset => asset.Id).Take(take).ToListAsync(cancellationToken);
        var restored = 0; var unavailable = 0; var errors = 0;
        foreach (var asset in assets)
        {
            try
            {
                if (await CanOpenAsync(asset, cancellationToken))
                {
                    Restore(asset);
                    QueueProcessing(asset);
                    restored++;
                }
                else
                {
                    MarkUnavailable(asset, MediaAvailabilityStatus.SourceMissing,
                        asset.UnavailableReason ?? "The source media could not be opened.");
                    unavailable++;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                MarkUnavailable(asset, MediaAvailabilityStatus.AccessDenied, ex.GetBaseException().Message);
                unavailable++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errors++;
                asset.LastAvailabilityCheckUtc = DateTimeOffset.UtcNow;
                _logger.LogWarning(ex, "Availability recheck failed for media asset {AssetId}", asset.Id);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        var hasMore = !assetId.HasValue && await query.Skip(take).AnyAsync(cancellationToken);
        return new(assets.Count, restored, unavailable, errors, hasMore);
    }

    public async Task<MediaAvailabilityReconciliationResult> ReconcileHistoricalAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(batchSize, 1, MaximumBatchSize);
        var sourceUnavailableCode = nameof(MediaContentUnavailableException);
        var candidates = await _db.Assets
            .Include(asset => asset.Source)
            .Include(asset => asset.ProcessingJobs)
            .Where(asset => !asset.IsDeleted
                            && asset.AvailabilityStatus == MediaAvailabilityStatus.Available
                            && asset.IsAvailable
                            && asset.ProcessingJobs.Any(job =>
                                job.Status == MediaProcessingJobStatus.DeadLetter
                                && job.FailureCode == sourceUnavailableCode))
            .OrderBy(asset => asset.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var restored = 0; var marked = 0; var errors = 0;
        foreach (var asset in candidates)
        {
            try
            {
                if (await CanOpenAsync(asset, cancellationToken))
                {
                    Restore(asset);
                    QueueProcessing(asset);
                    restored++;
                }
                else
                {
                    var failure = asset.ProcessingJobs
                        .Where(job => job.FailureCode == sourceUnavailableCode)
                        .OrderByDescending(job => job.UpdatedAtUtc)
                        .FirstOrDefault();
                    MarkUnavailable(asset, MediaAvailabilityStatus.SourceMissing,
                        failure?.FailureMessage ?? "The source media is no longer available.");
                    marked++;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                MarkUnavailable(asset, MediaAvailabilityStatus.AccessDenied, ex.GetBaseException().Message);
                marked++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errors++;
                asset.LastAvailabilityCheckUtc = DateTimeOffset.UtcNow;
                _logger.LogWarning(ex, "Historical availability reconciliation failed for media asset {AssetId}", asset.Id);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        var hasMore = await _db.Assets.AnyAsync(asset => !asset.IsDeleted
            && asset.AvailabilityStatus == MediaAvailabilityStatus.Available
            && asset.IsAvailable
            && asset.ProcessingJobs.Any(job => job.Status == MediaProcessingJobStatus.DeadLetter
                && job.FailureCode == sourceUnavailableCode), cancellationToken);
        return new(candidates.Count, restored, marked, errors, hasMore);
    }

    private async Task<bool> CanOpenAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var descriptor = await _contentResolver.ResolveAsync(asset, cancellationToken);
        if (descriptor is null) return false;
        await using var stream = await descriptor.OpenReadAsync(cancellationToken);
        var buffer = new byte[1];
        return await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) > 0;
    }

    private static void Restore(MediaAsset asset)
    {
        var now = DateTimeOffset.UtcNow;
        asset.IsAvailable = true;
        asset.AvailabilityStatus = MediaAvailabilityStatus.Available;
        asset.UnavailableReason = null;
        asset.UnavailableSinceUtc = null;
        asset.LastAvailabilityCheckUtc = now;
        asset.ProcessingFailureReason = null;
        asset.DerivativeStatus = MediaProcessingStatus.Pending;
        asset.AnalysisStatus = MediaProcessingStatus.Pending;
        asset.ContentHash = null;
        asset.Width = null;
        asset.Height = null;
        asset.AnalysedAtUtc = null;
    }

    private static void MarkUnavailable(MediaAsset asset, MediaAvailabilityStatus status, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        asset.IsAvailable = false;
        asset.AvailabilityStatus = status;
        asset.UnavailableReason = Trim(reason, 2048);
        asset.UnavailableSinceUtc ??= now;
        asset.LastAvailabilityCheckUtc = now;
        asset.DerivativeStatus = MediaProcessingStatus.Failed;
        asset.AnalysisStatus = MediaProcessingStatus.Failed;
        asset.ProcessingFailureReason = MediaProcessingFailurePolicy.MarkSourceUnavailable(reason);
    }

    private void QueueProcessing(MediaAsset asset)
    {
        var now = DateTimeOffset.UtcNow;
        var job = asset.ProcessingJobs.FirstOrDefault(job => job.JobType == MediaProcessingJobType.AnalyseAsset);
        if (job is null)
        {
            asset.ProcessingJobs.Add(new MediaProcessingJob
            {
                JobType = MediaProcessingJobType.AnalyseAsset,
                Status = MediaProcessingJobStatus.Pending,
                AttemptCount = 0,
                MaxAttempts = 5,
                AvailableAfterUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            return;
        }
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

    private static string Trim(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
