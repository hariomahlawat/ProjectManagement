using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Idempotent queue coordinator for face analysis. Only assets that have not been
/// analysed by the current detector/embedder pair are selected automatically.
/// </summary>
public sealed class FaceQueueService : IFaceQueueService
{
    private readonly MediaLibraryDbContext _db;
    private readonly MediaLibraryOptions _options;
    private readonly IFaceModelReadinessService _readiness;
    private readonly IFaceEligibilityPolicy _eligibility;
    private readonly ILogger<FaceQueueService> _logger;

    public FaceQueueService(
        MediaLibraryDbContext db,
        IOptions<MediaLibraryOptions> options,
        IFaceModelReadinessService readiness,
        IFaceEligibilityPolicy eligibility,
        ILogger<FaceQueueService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> QueueEligibleAsync(int limit, CancellationToken cancellationToken)
    {
        if (!await CanQueueAsync(cancellationToken))
        {
            return 0;
        }

        var version = CurrentAnalysisVersion;
        var maximum = Math.Clamp(limit, 1, 1_000);
        var eligibilityPredicate = _eligibility.BuildEligiblePredicate();
        var assetIds = await _db.Assets
            .AsNoTracking()
            .Where(eligibilityPredicate)
            .Where(asset => asset.FaceAnalysisVersion != version)
            .Where(asset => !asset.Faces.Any(face =>
                face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)))
            .Where(asset => !asset.ProcessingJobs.Any(job =>
                job.JobType == MediaProcessingJobType.DetectFaces
                && (job.Status == MediaProcessingJobStatus.Pending
                    || job.Status == MediaProcessingJobStatus.Running)))
            .OrderByDescending(asset => asset.MediaDateUtc)
            .ThenBy(asset => asset.Id)
            .Select(asset => asset.Id)
            .Take(maximum)
            .ToListAsync(cancellationToken);

        var queued = 0;
        foreach (var assetId in assetIds)
        {
            if (await QueueAssetCoreAsync(assetId, force: false, cancellationToken))
            {
                queued++;
            }
        }

        return queued;
    }

    public async Task<bool> QueueAssetAsync(long assetId, CancellationToken cancellationToken)
    {
        if (!await CanQueueAsync(cancellationToken))
        {
            return false;
        }

        return await QueueAssetCoreAsync(assetId, force: true, cancellationToken);
    }

    private async Task<bool> QueueAssetCoreAsync(
        long assetId,
        bool force,
        CancellationToken cancellationToken)
    {
        var asset = await _db.Assets
            .Include(item => item.ProcessingJobs)
            .Include(item => item.Faces)
                .ThenInclude(face => face.PersonAssignments)
            .SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return false;
        }

        var eligibility = _eligibility.Evaluate(asset);
        if (!eligibility.IsEligible)
        {
            _logger.LogInformation(
                "Face analysis was not queued for asset {AssetId}: {EligibilityCode} - {EligibilityReason}",
                assetId,
                eligibility.Code,
                eligibility.Reason);
            return false;
        }

        if (asset.Faces.Any(face => face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)))
        {
            _logger.LogInformation(
                "Face analysis was not queued for asset {AssetId} because it contains a human-reviewed identity.",
                assetId);
            return false;
        }

        if (!force && string.Equals(asset.FaceAnalysisVersion, CurrentAnalysisVersion, StringComparison.Ordinal))
        {
            return false;
        }

        var job = asset.ProcessingJobs.SingleOrDefault(
            item => item.JobType == MediaProcessingJobType.DetectFaces);
        var now = DateTimeOffset.UtcNow;
        if (job is null)
        {
            job = new MediaProcessingJob
            {
                MediaAssetId = assetId,
                JobType = MediaProcessingJobType.DetectFaces,
                Status = MediaProcessingJobStatus.Pending,
                MaxAttempts = Math.Max(3, _options.Processing.MaxAttempts),
                AvailableAfterUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.ProcessingJobs.Add(job);
        }
        else if (job.Status == MediaProcessingJobStatus.Pending
                 || job.Status == MediaProcessingJobStatus.Running)
        {
            return false;
        }
        else
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
        // Record the model pair at queue time. Retries are owned by the job worker;
        // this prevents the discovery worker from immediately resurrecting a dead-letter
        // job, while a future model upgrade still becomes eligible automatically.
        asset.FaceAnalysisVersion = CurrentAnalysisVersion;
        asset.FaceProcessingFailureReason = null;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> CanQueueAsync(CancellationToken cancellationToken)
    {
        if (!_options.People.Enabled || !_options.People.WorkerEnabled)
        {
            return false;
        }

        return (await _readiness.CheckAsync(cancellationToken)).IsReady;
    }

    private string CurrentAnalysisVersion
        => $"{_options.People.Detector.Key}:{_options.People.Detector.Version}|{_options.People.Embedder.Key}:{_options.People.Embedder.Version}";
}
