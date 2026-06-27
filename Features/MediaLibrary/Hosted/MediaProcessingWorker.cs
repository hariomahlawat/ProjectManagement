using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

public sealed class MediaProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<MediaProcessingWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public MediaProcessingWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MediaLibraryOptions> options,
        ILogger<MediaProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;

            try
            {
                for (var index = 0; index < _options.ProcessingBatchSize; index++)
                {
                    var job = await ClaimNextAsync(stoppingToken);
                    if (job is null)
                    {
                        break;
                    }

                    processed++;
                    await ProcessAsync(job, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Media processing worker cycle failed");
            }

            if (processed == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IdleDelaySeconds), stoppingToken);
            }
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
                        OR (""Status"" = 'Running' AND ""LockExpiresAtUtc"" < {now})
                      )
                  AND ""AvailableAfterUtc"" <= {now}
                ORDER BY ""AvailableAfterUtc"", ""Id""
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
        job.LockedBy = _workerId;
        job.LockExpiresAtUtc = now.AddMinutes(15);
        job.FailureCode = null;
        job.FailureMessage = null;
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
            var entity = await db.ProcessingJobs.SingleAsync(item => item.Id == job.Id, cancellationToken);
            entity.Status = MediaProcessingJobStatus.Completed;
            entity.CompletedAtUtc = DateTimeOffset.UtcNow;
            entity.LockedBy = null;
            entity.LockExpiresAtUtc = null;
            entity.FailureCode = null;
            entity.FailureMessage = null;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            using var failureScope = _scopeFactory.CreateScope();
            var db = failureScope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            var entity = await db.ProcessingJobs.SingleAsync(item => item.Id == job.Id, CancellationToken.None);
            var deadLetter = job.AttemptCount >= job.MaxAttempts;
            entity.Status = deadLetter
                ? MediaProcessingJobStatus.DeadLetter
                : MediaProcessingJobStatus.Pending;
            entity.AvailableAfterUtc = DateTimeOffset.UtcNow.Add(GetRetryDelay(job.AttemptCount));
            entity.LockedBy = null;
            entity.LockExpiresAtUtc = null;
            entity.FailureCode = ex.GetType().Name;
            entity.FailureMessage = Trim(ex.GetBaseException().Message, 2048);
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);

            _logger.LogWarning(
                ex,
                "Media job {JobId} failed on attempt {Attempt}/{MaxAttempts}; dead-letter={DeadLetter}",
                job.Id,
                job.AttemptCount,
                job.MaxAttempts,
                deadLetter);
        }
    }

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
