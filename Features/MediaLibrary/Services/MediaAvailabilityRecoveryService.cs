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

public interface IMediaAvailabilityRecoveryService
{
    Task<MediaAvailabilityRecoveryResult> RecheckAsync(
        long? assetId,
        int batchSize,
        CancellationToken cancellationToken);
}

/// <summary>
/// Re-validates catalogue assets whose physical content was previously unavailable.
/// Successful recovery is idempotent and requeues the existing analysis job (or creates one).
/// </summary>
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
            .Where(asset => !asset.IsAvailable
                            && !asset.IsDeleted
                            && asset.ProcessingFailureReason != null
                            && asset.ProcessingFailureReason.StartsWith(
                                MediaProcessingFailurePolicy.SourceUnavailableMarker));

        if (assetId.HasValue)
        {
            query = query.Where(asset => asset.Id == assetId.Value);
        }

        var assets = await query
            .OrderBy(asset => asset.LastSeenAtUtc)
            .ThenBy(asset => asset.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var examined = 0;
        var restored = 0;
        var unavailable = 0;
        var errors = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            examined++;

            try
            {
                var content = await _contentResolver.ResolveAsync(asset, cancellationToken);
                if (content is null)
                {
                    unavailable++;
                    continue;
                }

                await using var stream = await content.OpenReadAsync(cancellationToken);
                if (stream is null || !stream.CanRead)
                {
                    unavailable++;
                    continue;
                }

                var probe = new byte[1];
                var bytesRead = await stream.ReadAsync(probe.AsMemory(0, 1), cancellationToken);
                if (bytesRead == 0)
                {
                    unavailable++;
                    continue;
                }

                RestoreAsset(asset, now);
                await ResetOrCreateJobAsync(asset, now, cancellationToken);
                restored++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (MediaProcessingFailurePolicy.IsSourceUnavailable(ex))
            {
                unavailable++;
                asset.ProcessingFailureReason = MediaProcessingFailurePolicy.MarkSourceUnavailable(
                    Trim(ex.GetBaseException().Message, 1900));
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex, "Unable to recheck media asset {AssetId}", asset.Id);
            }
        }

        if (restored > 0)
        {
            var sourceIds = assets.Where(asset => asset.IsAvailable).Select(asset => asset.SourceId).Distinct().ToArray();
            foreach (var sourceId in sourceIds)
            {
                var source = await _db.Sources.SingleAsync(item => item.Id == sourceId, cancellationToken);
                source.IndexedAssetCount = await _db.Assets.CountAsync(
                    item => item.SourceId == sourceId && item.IsAvailable && !item.IsDeleted,
                    cancellationToken);
                source.UpdatedAtUtc = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var examinedIds = assets.Select(item => item.Id).ToArray();
        var hasMore = !assetId.HasValue && await _db.Assets.AnyAsync(
            asset => !asset.IsAvailable
                     && !asset.IsDeleted
                     && asset.ProcessingFailureReason != null
                     && asset.ProcessingFailureReason.StartsWith(
                         MediaProcessingFailurePolicy.SourceUnavailableMarker)
                     && !examinedIds.Contains(asset.Id),
            cancellationToken);

        return new MediaAvailabilityRecoveryResult(examined, restored, unavailable, errors, hasMore);
    }

    private static void RestoreAsset(MediaAsset asset, DateTimeOffset now)
    {
        asset.IsAvailable = true;
        asset.ProcessingFailureReason = null;
        asset.ContentHash = null;
        asset.CacheVersion++;

        if (asset.Kind == MediaAssetKind.Photo)
        {
            asset.DerivativeStatus = MediaProcessingStatus.Pending;
            asset.AnalysisStatus = MediaProcessingStatus.Pending;
            asset.ClassificationConfidence = null;
            asset.AnalysisSignalsJson = null;
            asset.AnalysedAtUtc = null;
        }

        asset.LastSeenAtUtc = now;
    }

    private async Task ResetOrCreateJobAsync(
        MediaAsset asset,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (asset.Kind != MediaAssetKind.Photo)
        {
            return;
        }

        var job = await _db.ProcessingJobs.SingleOrDefaultAsync(
            item => item.MediaAssetId == asset.Id
                    && item.JobType == MediaProcessingJobType.AnalyseAsset,
            cancellationToken);

        if (job is null)
        {
            _db.ProcessingJobs.Add(new MediaProcessingJob
            {
                MediaAssetId = asset.Id,
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

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
