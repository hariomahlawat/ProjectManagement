using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

public sealed class MediaProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaLibraryOptions _options;
    private readonly IMediaProcessingRuntimeState _runtime;
    private readonly ILogger<MediaProcessingWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public MediaProcessingWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MediaLibraryOptions> options,
        IMediaProcessingRuntimeState runtime,
        ILogger<MediaProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }


    private bool FaceOnlyMode =>
        !_options.IsProcessingWorkerEnabled && _options.IsPeopleWorkerEnabled;

    private int EffectiveBatchSize => FaceOnlyMode
        ? Math.Clamp(_options.People.BatchSize, 1, 16)
        : Math.Clamp(_options.Processing.BatchSize, 1, 16);

    private int EffectiveIdleDelaySeconds => FaceOnlyMode
        ? Math.Clamp(_options.People.IdleDelaySeconds, 1, 3600)
        : Math.Clamp(_options.Processing.IdleDelaySeconds, 1, 3600);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtime.MarkStarted(_workerId);
        _logger.LogInformation(
            "Media processing worker {WorkerId} started. BatchSize={BatchSize}, IdleDelaySeconds={IdleDelaySeconds}",
            _workerId,
            EffectiveBatchSize,
            EffectiveIdleDelaySeconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var processed = 0;
                var idleDelaySeconds = EffectiveIdleDelaySeconds;

                try
                {
                    _runtime.Heartbeat("Checking schema");
                    if (!await IsSchemaReadyAsync(stoppingToken))
                    {
                        idleDelaySeconds = Math.Max(60, idleDelaySeconds);
                        _runtime.Heartbeat("Waiting for catalogue schema");
                        processed = 0;
                    }
                    else
                    {
                        _runtime.Heartbeat("Polling");
                        await RecoverExpiredLocksAsync(stoppingToken);

                        for (var index = 0; index < EffectiveBatchSize; index++)
                        {
                            var job = await ClaimNextAsync(stoppingToken);
                            if (job is null)
                            {
                                break;
                            }

                            processed++;
                            _runtime.MarkClaimed(job.Id, job.MediaAssetId);
                            await ProcessAsync(job, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (IsCatalogueInfrastructureFailure(ex))
                {
                    idleDelaySeconds = Math.Max(60, idleDelaySeconds);
                    _runtime.MarkFailed(0, ex);
                    _logger.LogWarning(ex,
                        "Optional media processing catalogue is unavailable; worker {WorkerId} will retry later",
                        _workerId);
                }
                catch (Exception ex)
                {
                    _runtime.MarkFailed(0, ex);
                    _logger.LogError(ex, "Media processing worker {WorkerId} cycle failed", _workerId);
                }

                if (processed == 0)
                {
                    _runtime.MarkIdle();
                    await Task.Delay(TimeSpan.FromSeconds(idleDelaySeconds), stoppingToken);
                }
            }
        }
        finally
        {
            _runtime.Heartbeat("Stopped");
            _logger.LogInformation("Media processing worker {WorkerId} stopped", _workerId);
        }
    }

    private async Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var schema = scope.ServiceProvider.GetRequiredService<IMediaLibrarySchemaService>();
        var status = await schema.GetStatusAsync(cancellationToken);
        return status.IsAvailable && status.IsCurrent;
    }

    private async Task RecoverExpiredLocksAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
        var now = DateTimeOffset.UtcNow;

        var recovered = await db.ProcessingJobs
            .Where(job => job.Status == MediaProcessingJobStatus.Running
                          && job.LockExpiresAtUtc != null
                          && job.LockExpiresAtUtc < now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, MediaProcessingJobStatus.Pending)
                .SetProperty(job => job.AvailableAfterUtc, now)
                .SetProperty(job => job.LockedBy, (string?)null)
                .SetProperty(job => job.LockExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(job => job.FailureCode, "ExpiredWorkerLock")
                .SetProperty(job => job.FailureMessage, "The previous worker lock expired before completion; the job was recovered automatically.")
                .SetProperty(job => job.UpdatedAtUtc, now), cancellationToken);

        if (recovered > 0)
        {
            _logger.LogWarning("Recovered {Count} expired media processing job lock(s)", recovered);
        }
    }

    private async Task<ClaimedJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var query = FaceOnlyMode
            ? db.ProcessingJobs.FromSqlInterpolated($@"
                SELECT *
                FROM ""MediaProcessingJobs""
                WHERE ""JobType"" IN ('DetectFaces', 'GenerateFaceEmbeddings', 'AssignFaceCluster')
                  AND (
                        ""Status"" = 'Pending'
                        OR (""Status"" = 'Running' AND ""LockExpiresAtUtc"" IS NOT NULL AND ""LockExpiresAtUtc"" < {now})
                      )
                  AND (""AvailableAfterUtc"" IS NULL OR ""AvailableAfterUtc"" <= {now})
                ORDER BY COALESCE(""AvailableAfterUtc"", ""CreatedAtUtc""), ""Id""
                FOR UPDATE SKIP LOCKED
                LIMIT 1")
            : _options.IsPeopleWorkerEnabled
                ? db.ProcessingJobs.FromSqlInterpolated($@"
                    SELECT *
                    FROM ""MediaProcessingJobs""
                    WHERE (
                            ""Status"" = 'Pending'
                            OR (""Status"" = 'Running' AND ""LockExpiresAtUtc"" IS NOT NULL AND ""LockExpiresAtUtc"" < {now})
                          )
                      AND (""AvailableAfterUtc"" IS NULL OR ""AvailableAfterUtc"" <= {now})
                    ORDER BY COALESCE(""AvailableAfterUtc"", ""CreatedAtUtc""), ""Id""
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1")
                : db.ProcessingJobs.FromSqlInterpolated($@"
                    SELECT *
                    FROM ""MediaProcessingJobs""
                    WHERE ""JobType"" NOT IN ('DetectFaces', 'GenerateFaceEmbeddings', 'AssignFaceCluster')
                      AND (
                            ""Status"" = 'Pending'
                            OR (""Status"" = 'Running' AND ""LockExpiresAtUtc"" IS NOT NULL AND ""LockExpiresAtUtc"" < {now})
                          )
                      AND (""AvailableAfterUtc"" IS NULL OR ""AvailableAfterUtc"" <= {now})
                    ORDER BY COALESCE(""AvailableAfterUtc"", ""CreatedAtUtc""), ""Id""
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1");
        var claimed = await query.ToListAsync(cancellationToken);
        var job = claimed.SingleOrDefault();

        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        job.Status = MediaProcessingJobStatus.Running;
        job.AttemptCount++;
        job.StartedAtUtc = now;
        job.CompletedAtUtc = null;
        job.LockedBy = _workerId;
        job.LockExpiresAtUtc = now.AddMinutes(15);
        job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ClaimedJob(job.Id, job.MediaAssetId, job.JobType, job.AttemptCount, job.MaxAttempts);
    }

    private async Task ProcessAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseTask = RenewLeaseLoopAsync(job.Id, leaseCancellation.Token);
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<IMediaAssetProcessor>();
                await processor.ProcessAsync(job.MediaAssetId, job.JobType, cancellationToken);
            }

            using var completionScope = _scopeFactory.CreateScope();
            var db = completionScope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            var entity = await db.ProcessingJobs.SingleOrDefaultAsync(
                item => item.Id == job.Id,
                cancellationToken);
            if (entity is null)
            {
                _logger.LogWarning(
                    "Media processing job {JobId} disappeared before completion could be recorded",
                    job.Id);
                return;
            }

            if (entity.Status != MediaProcessingJobStatus.Running
                || !string.Equals(entity.LockedBy, _workerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Worker lease for media job {job.Id} was lost before completion.");
            }

            entity.Status = MediaProcessingJobStatus.Completed;
            entity.CompletedAtUtc = DateTimeOffset.UtcNow;
            entity.LockedBy = null;
            entity.LockExpiresAtUtc = null;
            entity.FailureCode = null;
            entity.FailureMessage = null;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            _runtime.MarkCompleted(job.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseCancelledJobAsync(job.Id);
            throw;
        }
        catch (Exception ex)
        {
            var sourceUnavailable = await RecordFailureAsync(job, ex);
            if (sourceUnavailable)
            {
                _runtime.MarkUnavailable(job.Id);
            }
            else
            {
                _runtime.MarkFailed(job.Id, ex);
            }
        }
        finally
        {
            leaseCancellation.Cancel();
            try
            {
                await leaseTask;
            }
            catch (OperationCanceledException) when (leaseCancellation.IsCancellationRequested)
            {
                // Normal lease-loop shutdown.
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Lease-renewal loop for media job {JobId} ended unexpectedly.",
                    job.Id);
            }
        }
    }

    private async Task RenewLeaseLoopAsync(long jobId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
                var now = DateTimeOffset.UtcNow;
                var renewed = await db.ProcessingJobs
                    .Where(job => job.Id == jobId
                                  && job.Status == MediaProcessingJobStatus.Running
                                  && job.LockedBy == _workerId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(job => job.LockExpiresAtUtc, now.AddMinutes(15))
                        .SetProperty(job => job.UpdatedAtUtc, now), cancellationToken);
                if (renewed == 0)
                {
                    _logger.LogWarning(
                        "Could not renew lease for media job {JobId}; the job is no longer owned by worker {WorkerId}",
                        jobId,
                        _workerId);
                    return;
                }

                _runtime.Heartbeat($"Processing job {jobId}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A transient database outage must not terminate processing immediately. The
                // completion path still verifies ownership before committing the job result.
                _logger.LogWarning(
                    exception,
                    "Could not renew the lease for media job {JobId}; retrying on the next interval.",
                    jobId);
            }
        }
    }

    private async Task ReleaseCancelledJobAsync(long jobId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            var entity = await db.ProcessingJobs.SingleOrDefaultAsync(item => item.Id == jobId, CancellationToken.None);
            if (entity is null
                || entity.Status != MediaProcessingJobStatus.Running
                || !string.Equals(entity.LockedBy, _workerId, StringComparison.Ordinal))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            entity.Status = MediaProcessingJobStatus.Pending;
            entity.AvailableAfterUtc = now.AddMinutes(1);
            entity.LockedBy = null;
            entity.LockExpiresAtUtc = null;
            entity.FailureCode = "WorkerStopping";
            entity.FailureMessage = "Processing was interrupted because the application was stopping. The job will resume automatically.";
            entity.UpdatedAtUtc = now;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release cancelled media job {JobId}", jobId);
        }
    }

    private async Task<bool> RecordFailureAsync(ClaimedJob job, Exception ex)
    {
        using var failureScope = _scopeFactory.CreateScope();
        var db = failureScope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
        var entity = await db.ProcessingJobs.SingleOrDefaultAsync(item => item.Id == job.Id, CancellationToken.None);
        if (entity is null)
        {
            _logger.LogError(ex, "Media job {JobId} failed but its database row no longer exists", job.Id);
            return false;
        }
        if (entity.Status != MediaProcessingJobStatus.Running
            || !string.Equals(entity.LockedBy, _workerId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                ex,
                "Media job {JobId} failed after worker {WorkerId} lost ownership; the current owner state was not overwritten.",
                job.Id,
                _workerId);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var permanent = MediaProcessingFailurePolicy.IsPermanent(ex);
        var sourceUnavailable = MediaProcessingFailurePolicy.IsSourceUnavailable(ex);
        var deadLetter = !sourceUnavailable
                         && (permanent || job.AttemptCount >= Math.Max(1, job.MaxAttempts));
        entity.Status = sourceUnavailable
            ? MediaProcessingJobStatus.Completed
            : deadLetter
                ? MediaProcessingJobStatus.DeadLetter
                : MediaProcessingJobStatus.Pending;
        entity.CompletedAtUtc = sourceUnavailable ? now : null;
        entity.AvailableAfterUtc = sourceUnavailable || deadLetter
            ? now
            : now.Add(GetRetryDelay(job.AttemptCount));
        entity.LockedBy = null;
        entity.LockExpiresAtUtc = null;
        entity.FailureCode = sourceUnavailable
            ? "SourceUnavailable"
            : ex.GetType().Name;
        var failureMessage = Trim(ex.GetBaseException().Message, 2048);
        entity.FailureMessage = failureMessage;
        entity.UpdatedAtUtc = now;

        if (sourceUnavailable || IsFaceJobType(job.JobType))
        {
            var asset = await db.Assets.SingleOrDefaultAsync(
                item => item.Id == job.MediaAssetId,
                CancellationToken.None);
            if (asset is not null)
            {
                if (IsFaceJobType(job.JobType))
                {
                    asset.FaceAnalysisStatus = MediaProcessingStatus.Failed;
                    asset.FaceProcessingFailureReason = failureMessage;
                }

                if (sourceUnavailable)
                {
                    asset.IsAvailable = false;
                    asset.AvailabilityStatus = ex.GetBaseException() is UnauthorizedAccessException
                        ? MediaAvailabilityStatus.AccessDenied
                        : MediaAvailabilityStatus.SourceMissing;
                    asset.UnavailableReason = failureMessage;
                    asset.UnavailableSinceUtc ??= now;
                    asset.LastAvailabilityCheckUtc = now;
                    asset.DerivativeStatus = MediaProcessingStatus.Failed;
                    asset.AnalysisStatus = MediaProcessingStatus.Failed;
                    asset.FaceAnalysisStatus = MediaProcessingStatus.Failed;
                    var marked = MediaProcessingFailurePolicy.MarkSourceUnavailable(failureMessage);
                    asset.FaceProcessingFailureReason = marked;
                    asset.ProcessingFailureReason = marked;
                }
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);

        if (sourceUnavailable)
        {
            _logger.LogInformation(
                "Media job {JobId} completed without processing because its source is unavailable: {Message}",
                job.Id,
                failureMessage);
        }
        else if (deadLetter)
        {
            _logger.LogError(
                ex,
                "Media job {JobId} was moved to dead-letter on attempt {Attempt}/{MaxAttempts}; permanent={Permanent}",
                job.Id,
                job.AttemptCount,
                job.MaxAttempts,
                permanent);
        }
        else
        {
            _logger.LogWarning(
                ex,
                "Media job {JobId} failed on attempt {Attempt}/{MaxAttempts}; retry scheduled at {AvailableAfterUtc}",
                job.Id,
                job.AttemptCount,
                job.MaxAttempts,
                entity.AvailableAfterUtc);
        }

        return sourceUnavailable;
    }


    private static bool IsFaceJobType(MediaProcessingJobType jobType)
        => jobType is MediaProcessingJobType.DetectFaces
            or MediaProcessingJobType.GenerateFaceEmbeddings
            or MediaProcessingJobType.AssignFaceCluster;

    private static bool IsCatalogueInfrastructureFailure(Exception exception)
        => exception is NpgsqlException
            or DbUpdateException
            or TimeoutException;

    private static TimeSpan GetRetryDelay(int attempt)
    {
        var baseline = attempt switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            4 => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(6)
        };

        // Jitter prevents a large failed batch from retrying on the same instant.
        return baseline.Add(TimeSpan.FromSeconds(Random.Shared.Next(5, 46)));
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record ClaimedJob(
        long Id,
        long MediaAssetId,
        MediaProcessingJobType JobType,
        int AttemptCount,
        int MaxAttempts);
}
