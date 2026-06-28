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
    int TemporarilyUnavailable,
    int Errors,
    bool HasMore);

public sealed record MediaAvailabilityReconciliationStatus(
    int HistoricalCandidates,
    int AvailableAssets,
    int UnavailableAssets,
    DateTimeOffset? LastAvailabilityCheckUtc);

public interface IMediaAvailabilityRecoveryService
{
    Task<MediaAvailabilityRecoveryResult> RecheckAsync(long? assetId, int batchSize, CancellationToken cancellationToken);
    Task<MediaAvailabilityReconciliationResult> ReconcileHistoricalAsync(int batchSize, CancellationToken cancellationToken);
    Task<MediaAvailabilityReconciliationStatus> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns source-availability reconciliation. Processing-job history is retained for audit,
/// while MediaAsset is the authoritative state used by the Photos timeline.
/// </summary>
public sealed class MediaAvailabilityRecoveryService : IMediaAvailabilityRecoveryService
{
    private const int MaximumBatchSize = 250;

    private static readonly string[] SourceUnavailableFailureCodes =
    {
        nameof(MediaContentUnavailableException),
        nameof(FileNotFoundException),
        nameof(DirectoryNotFoundException)
    };

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

    public async Task<MediaAvailabilityReconciliationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var candidates = await HistoricalCandidatesQuery().CountAsync(cancellationToken);
        var available = await _db.Assets.CountAsync(asset =>
            !asset.IsDeleted
            && asset.IsAvailable
            && asset.AvailabilityStatus == MediaAvailabilityStatus.Available,
            cancellationToken);
        var unavailable = await _db.Assets.CountAsync(asset =>
            !asset.IsDeleted
            && (!asset.IsAvailable || asset.AvailabilityStatus != MediaAvailabilityStatus.Available),
            cancellationToken);
        var lastChecked = await _db.Assets
            .Where(asset => !asset.IsDeleted && asset.LastAvailabilityCheckUtc != null)
            .MaxAsync(asset => (DateTimeOffset?)asset.LastAvailabilityCheckUtc, cancellationToken);

        return new(candidates, available, unavailable, lastChecked);
    }

    public async Task<MediaAvailabilityRecoveryResult> RecheckAsync(
        long? assetId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(batchSize, 1, MaximumBatchSize);
        var query = _db.Assets
            .Include(asset => asset.Source)
            .Include(asset => asset.ProcessingJobs)
            .Where(asset => !asset.IsDeleted
                            && (!asset.IsAvailable || asset.AvailabilityStatus != MediaAvailabilityStatus.Available));

        if (assetId.HasValue)
        {
            query = query.Where(asset => asset.Id == assetId.Value);
        }

        var assets = await query
            .OrderBy(asset => asset.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var restored = 0;
        var unavailable = 0;
        var errors = 0;
        var affectedCollections = new HashSet<string>(StringComparer.Ordinal);

        foreach (var asset in assets)
        {
            var outcome = await ProbeAsync(asset, cancellationToken);
            switch (outcome.Status)
            {
                case MediaAvailabilityStatus.Available:
                    Restore(asset);
                    QueueProcessing(asset);
                    restored++;
                    break;

                case MediaAvailabilityStatus.SourceMissing:
                case MediaAvailabilityStatus.AccessDenied:
                case MediaAvailabilityStatus.TemporarilyUnavailable:
                case MediaAvailabilityStatus.Unsupported:
                case MediaAvailabilityStatus.Corrupt:
                    MarkUnavailable(asset, outcome.Status, outcome.Reason);
                    unavailable++;
                    break;

                default:
                    errors++;
                    asset.LastAvailabilityCheckUtc = DateTimeOffset.UtcNow;
                    break;
            }

            affectedCollections.Add(asset.CollectionKey);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await RepairCollectionCoversAsync(affectedCollections, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var lastId = assets.Count == 0 ? 0L : assets[^1].Id;
        var hasMore = !assetId.HasValue && await query
            .Where(asset => asset.Id > lastId)
            .AnyAsync(cancellationToken);

        return new(assets.Count, restored, unavailable, errors, hasMore);
    }

    public async Task<MediaAvailabilityReconciliationResult> ReconcileHistoricalAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(batchSize, 1, MaximumBatchSize);
        var candidates = await HistoricalCandidatesQuery()
            .Include(asset => asset.Source)
            .Include(asset => asset.ProcessingJobs)
            .OrderBy(asset => asset.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var restored = 0;
        var markedUnavailable = 0;
        var temporarilyUnavailable = 0;
        var errors = 0;
        var affectedCollections = new HashSet<string>(StringComparer.Ordinal);

        foreach (var asset in candidates)
        {
            try
            {
                var outcome = await ProbeAsync(asset, cancellationToken);
                if (outcome.Status == MediaAvailabilityStatus.Available)
                {
                    Restore(asset);
                    QueueProcessing(asset);
                    restored++;
                }
                else
                {
                    var historicalMessage = LatestSourceUnavailableFailure(asset)?.FailureMessage;
                    var reason = string.IsNullOrWhiteSpace(outcome.Reason)
                        ? historicalMessage ?? "The source media is unavailable."
                        : outcome.Reason;

                    MarkUnavailable(asset, outcome.Status, reason);
                    if (outcome.Status == MediaAvailabilityStatus.TemporarilyUnavailable)
                    {
                        temporarilyUnavailable++;
                    }
                    else
                    {
                        markedUnavailable++;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                asset.LastAvailabilityCheckUtc = DateTimeOffset.UtcNow;
                _logger.LogWarning(ex,
                    "Historical availability reconciliation failed for media asset {AssetId}", asset.Id);
            }

            affectedCollections.Add(asset.CollectionKey);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await RepairCollectionCoversAsync(affectedCollections, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var hasMore = await HistoricalCandidatesQuery().AnyAsync(cancellationToken);
        return new(candidates.Count, restored, markedUnavailable, temporarilyUnavailable, errors, hasMore);
    }

    private IQueryable<MediaAsset> HistoricalCandidatesQuery()
        => _db.Assets.Where(asset =>
            !asset.IsDeleted
            && asset.IsAvailable
            && asset.AvailabilityStatus == MediaAvailabilityStatus.Available
            && asset.ProcessingJobs.Any(job =>
                job.Status == MediaProcessingJobStatus.DeadLetter
                && job.FailureCode != null
                && SourceUnavailableFailureCodes.Contains(job.FailureCode)));

    private static MediaProcessingJob? LatestSourceUnavailableFailure(MediaAsset asset)
        => asset.ProcessingJobs
            .Where(job => job.FailureCode != null
                          && SourceUnavailableFailureCodes.Contains(job.FailureCode))
            .OrderByDescending(job => job.UpdatedAtUtc)
            .FirstOrDefault();

    private async Task<AvailabilityProbeOutcome> ProbeAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        try
        {
            var descriptor = await _contentResolver.ResolveAsync(asset, cancellationToken);
            if (descriptor is null)
            {
                return AvailabilityProbeOutcome.Missing("No readable source representation could be resolved.");
            }

            await using var stream = await descriptor.OpenReadAsync(cancellationToken);
            var buffer = new byte[1];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            return bytesRead > 0
                ? AvailabilityProbeOutcome.Available()
                : AvailabilityProbeOutcome.Corrupt("The source file is empty.");
        }
        catch (MediaContentUnavailableException ex)
        {
            return AvailabilityProbeOutcome.Missing(ex.GetBaseException().Message);
        }
        catch (FileNotFoundException ex)
        {
            return AvailabilityProbeOutcome.Missing(ex.GetBaseException().Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return AvailabilityProbeOutcome.Missing(ex.GetBaseException().Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return AvailabilityProbeOutcome.AccessDenied(ex.GetBaseException().Message);
        }
        catch (InvalidDataException ex)
        {
            return AvailabilityProbeOutcome.Corrupt(ex.GetBaseException().Message);
        }
        catch (NotSupportedException ex)
        {
            return AvailabilityProbeOutcome.Unsupported(ex.GetBaseException().Message);
        }
        catch (IOException ex)
        {
            return AvailabilityProbeOutcome.Temporary(ex.GetBaseException().Message);
        }
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
        asset.IsCover = false;
        asset.DerivativeStatus = status is MediaAvailabilityStatus.TemporarilyUnavailable or MediaAvailabilityStatus.AccessDenied
            ? MediaProcessingStatus.Pending
            : MediaProcessingStatus.Failed;
        asset.AnalysisStatus = asset.DerivativeStatus;
        asset.ProcessingFailureReason = MediaProcessingFailurePolicy.MarkSourceUnavailable(reason);
    }

    private void QueueProcessing(MediaAsset asset)
    {
        var now = DateTimeOffset.UtcNow;
        var job = asset.ProcessingJobs
            .Where(job => job.JobType == MediaProcessingJobType.AnalyseAsset)
            .OrderByDescending(job => job.UpdatedAtUtc)
            .FirstOrDefault();

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

    private async Task RepairCollectionCoversAsync(
        IReadOnlyCollection<string> collectionKeys,
        CancellationToken cancellationToken)
    {
        if (collectionKeys.Count == 0)
        {
            return;
        }

        foreach (var collectionKey in collectionKeys)
        {
            var available = await _db.Assets
                .Where(asset => asset.CollectionKey == collectionKey
                                && !asset.IsDeleted
                                && !asset.IsArchived
                                && asset.IsAvailable
                                && asset.AvailabilityStatus == MediaAvailabilityStatus.Available)
                .OrderByDescending(asset => asset.IsCover)
                .ThenBy(asset => asset.SortOrder)
                .ThenBy(asset => asset.Id)
                .ToListAsync(cancellationToken);

            if (available.Count == 0)
            {
                continue;
            }

            var selected = available.FirstOrDefault(asset => asset.IsCover) ?? available[0];
            foreach (var asset in available)
            {
                asset.IsCover = asset.Id == selected.Id;
            }
        }
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record AvailabilityProbeOutcome(MediaAvailabilityStatus Status, string Reason)
    {
        public static AvailabilityProbeOutcome Available() => new(MediaAvailabilityStatus.Available, string.Empty);
        public static AvailabilityProbeOutcome Missing(string reason) => new(MediaAvailabilityStatus.SourceMissing, reason);
        public static AvailabilityProbeOutcome AccessDenied(string reason) => new(MediaAvailabilityStatus.AccessDenied, reason);
        public static AvailabilityProbeOutcome Temporary(string reason) => new(MediaAvailabilityStatus.TemporarilyUnavailable, reason);
        public static AvailabilityProbeOutcome Unsupported(string reason) => new(MediaAvailabilityStatus.Unsupported, reason);
        public static AvailabilityProbeOutcome Corrupt(string reason) => new(MediaAvailabilityStatus.Corrupt, reason);
    }
}
