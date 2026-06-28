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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtime.MarkStarted(_workerId);
        _logger.LogInformation(
            "Media processing worker {WorkerId} started. BatchSize={BatchSize}, IdleDelaySeconds={IdleDelaySeconds}",
            _workerId,
            _options.Processing.BatchSize,
            _options.Processing.IdleDelaySeconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var processed = 0;
                var idleDelaySeconds = Math.Max(1, _options.Processing.IdleDelaySeconds);

                try
                {
                    _runtime.Heartbeat("Polling");
                    await RecoverExpiredLocksAsync(stoppingToken);

                    for (var index = 0; index < Math.Max(1, _options.Processing.BatchSize); index++)
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
        var claimed = await db.ProcessingJobs
            .FromSqlInterpolated($@"
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
            .ToListAsync(cancellationToken);
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
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var processor = scope.ServiceProvider.GetRequiredService<IMediaAssetProcessor>();
                await processor.ProcessAsync(job.MediaAssetId, job.JobType, cancellationToken);
            }

            using var completionScope = _scopeFactory.CreateScope();
            var db = completionScope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            var entity = await db.ProcessingJobs.SingleOrDefaultAsync(item => item.Id == job.Id, cancellationToken);
            if (entity is null)
            {
                _logger.LogWarning("Media processing job {JobId} disappeared before completion could be recorded", job.Id);
                return;
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
            await RecordFailureAsync(job, ex);
            _runtime.MarkFailed(job.Id, ex);
        }
    }

    private async Task ReleaseCancelledJobAsync(long jobId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            var entity = await db.ProcessingJobs.SingleOrDefaultAsync(item => item.Id == jobId, CancellationToken.None);
            if (entity is null) return;

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

    private async Task RecordFailureAsync(ClaimedJob job, Exception ex)
    {
        using var failureScope = _scopeFactory.CreateScope();
        var db = failureScope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
        var entity = await db.ProcessingJobs.SingleOrDefaultAsync(item => item.Id == job.Id, CancellationToken.None);
        if (entity is null)
        {
            _logger.LogError(ex, "Media job {JobId} failed but its database row no longer exists", job.Id);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var permanent = ex is MediaContentUnavailableException or MediaProcessingPermanentException;
        var deadLetter = permanent || job.AttemptCount >= Math.Max(1, job.MaxAttempts);
        entity.Status = deadLetter
            ? MediaProcessingJobStatus.DeadLetter
            : MediaProcessingJobStatus.Pending;
        entity.AvailableAfterUtc = deadLetter ? now : now.Add(GetRetryDelay(job.AttemptCount));
        entity.LockedBy = null;
        entity.LockExpiresAtUtc = null;
        entity.FailureCode = ex.GetType().Name;
        entity.FailureMessage = Trim(ex.GetBaseException().Message, 2048);
        entity.UpdatedAtUtc = now;
        await db.SaveChangesAsync(CancellationToken.None);

        if (deadLetter)
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
    }

    private static bool IsCatalogueInfrastructureFailure(Exception exception)
        => exception is NpgsqlException
            or DbUpdateException
            or TimeoutException;

    private static TimeSpan GetRetryDelay(int attempt)
        => attempt switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            4 => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(6)
        };

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record ClaimedJob(
        long Id,
        long MediaAssetId,
        MediaProcessingJobType JobType,
        int AttemptCount,
        int MaxAttempts);
}
