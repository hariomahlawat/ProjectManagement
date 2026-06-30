using Microsoft.EntityFrameworkCore;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Outbox;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Durable, multi-instance-safe consumer for PRISM source-media changes. Database row locks
/// provide exclusive claims; expired leases are recovered automatically after a crash.
/// </summary>
public sealed class PrismMediaOutboxWorker : BackgroundService
{
    private const int BatchSize = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPrismMediaOutboxSignal _signal;
    private readonly IPrismMediaOutboxRuntimeState _runtimeState;
    private readonly ILogger<PrismMediaOutboxWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private DateTimeOffset _lastCleanupUtc = DateTimeOffset.MinValue;

    public PrismMediaOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IPrismMediaOutboxSignal signal,
        IPrismMediaOutboxRuntimeState runtimeState,
        ILogger<PrismMediaOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtimeState.MarkStarted();
        var backfillCompleted = false;
        var nextBackfillAttemptUtc = DateTimeOffset.MinValue;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _runtimeState.Heartbeat();
                try
                {
                    if (!backfillCompleted && DateTimeOffset.UtcNow >= nextBackfillAttemptUtc)
                    {
                        backfillCompleted = await RunStartupBackfillAsync(stoppingToken);
                        nextBackfillAttemptUtc = backfillCompleted
                            ? DateTimeOffset.MaxValue
                            : DateTimeOffset.UtcNow.AddMinutes(1);
                    }

                    var processed = await DrainOnceAsync(stoppingToken);
                    if (processed == 0)
                    {
                        await _signal.WaitAsync(PollInterval, stoppingToken);
                    }

                    if (DateTimeOffset.UtcNow - _lastCleanupUtc > TimeSpan.FromHours(1))
                    {
                        await CleanupCompletedAsync(stoppingToken);
                        _lastCleanupUtc = DateTimeOffset.UtcNow;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _runtimeState.MarkFailed(Trim(ex.GetBaseException().Message, 2048));
                    _logger.LogError(ex, "PRISM media outbox worker cycle failed");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        finally
        {
            _runtimeState.MarkStopped();
        }
    }

    private async Task<bool> RunStartupBackfillAsync(CancellationToken cancellationToken)
    {
        _runtimeState.MarkBackfillAttempt("Running");
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var schema = scope.ServiceProvider.GetRequiredService<IMediaLibrarySchemaService>();
            var status = await schema.GetStatusAsync(cancellationToken);
            if (!status.IsAvailable || !status.IsOperational)
            {
                _logger.LogWarning(
                    "PRISM media startup backfill deferred because the media schema is not operational. Reference={Reference}",
                    status.DiagnosticReference);
                _runtimeState.MarkBackfillAttempt("Waiting for media schema");
                return false;
            }

            var bootstrapper = scope.ServiceProvider.GetRequiredService<IMediaSourceBootstrapper>();
            await bootstrapper.EnsureConfiguredSourcesAsync(cancellationToken);

            // A corrected deployment should not wait for the previous exponential-delay window.
            // Pending events remain durable and keep their attempt count; only their next due time
            // is advanced so the worker can validate the repaired code path immediately.
            await ReleaseRetryablePendingEventsAsync(cancellationToken);

            try
            {
                var synchronizer = scope.ServiceProvider.GetRequiredService<IPrismMediaCatalogueSynchronizer>();
                await synchronizer.SynchronizeAsync(cancellationToken);
                _logger.LogInformation("PRISM media startup catalogue reconciliation completed");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A defect in any legacy source adapter must not prevent Activity photos from
                // entering the targeted durable pipeline.
                _logger.LogError(ex,
                    "PRISM startup catalogue reconciliation failed; targeted Activity backfill will continue");
            }

            var queued = await EnqueueMissingActivityPhotosAsync(cancellationToken);
            if (queued > 0)
            {
                _signal.Pulse();
                _logger.LogInformation(
                    "Queued {Count} missing Activity photo(s) for durable startup ingestion",
                    queued);
            }

            _runtimeState.MarkBackfillCompleted();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _runtimeState.MarkFailed(Trim(ex.GetBaseException().Message, 2048));
            _runtimeState.MarkBackfillAttempt("Retry scheduled");
            _logger.LogError(ex,
                "PRISM media startup backfill failed; it will be retried automatically");
            return false;
        }
    }

    private async Task ReleaseRetryablePendingEventsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var released = await db.PrismMediaOutboxMessages
            .Where(message => message.Status == PrismMediaOutboxStatus.Pending
                              && message.LastError != null
                              && message.AvailableAfterUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.AvailableAfterUtc, now),
                cancellationToken);

        if (released > 0)
        {
            _logger.LogInformation(
                "Released {Count} previously failed PRISM media event(s) for immediate retry after startup",
                released);
            _signal.Pulse();
        }
    }

    private async Task<int> EnqueueMissingActivityPhotosAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var applicationDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mediaDb = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();

        var sourceId = await mediaDb.Sources
            .AsNoTracking()
            .Where(source => source.Key == MediaSourceBootstrapper.PrismSourceKey && !source.IsDeleted)
            .Select(source => (Guid?)source.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var cataloguedSourceIds = sourceId.HasValue
            ? (await mediaDb.Assets
                .AsNoTracking()
                .Where(asset => asset.SourceId == sourceId.Value
                                && asset.Origin == MediaAssetOrigin.ActivityPhoto
                                && !asset.IsDeleted)
                .Select(asset => asset.SourceEntityId)
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var sourcePhotos = await applicationDb.ActivityAttachments
            .AsNoTracking()
            .Where(attachment => !attachment.Activity.IsDeleted)
            .Where(ActivityAttachmentClassifier.IsPhotoExpression)
            .Select(attachment => new
            {
                attachment.Id,
                attachment.ActivityId,
                attachment.StorageKey
            })
            .ToListAsync(cancellationToken);

        var outstandingIds = (await applicationDb.PrismMediaOutboxMessages
                .AsNoTracking()
                .Where(message => message.EventType == PrismMediaOutboxEventType.ActivityPhotoUpsert
                                  && message.Status != PrismMediaOutboxStatus.Completed
                                  && message.AttachmentId != null)
                .Select(message => message.AttachmentId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var outstandingStorageKeys = (await applicationDb.PrismMediaOutboxMessages
                .AsNoTracking()
                .Where(message => message.EventType == PrismMediaOutboxEventType.ActivityPhotoUpsert
                                  && message.Status != PrismMediaOutboxStatus.Completed
                                  && message.StorageKey != null)
                .Select(message => message.StorageKey!)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow;
        var queued = 0;
        foreach (var photo in sourcePhotos)
        {
            var sourceEntityId = $"activity-photo:{photo.Id}";
            if (cataloguedSourceIds.Contains(sourceEntityId)
                || outstandingIds.Contains(photo.Id)
                || outstandingStorageKeys.Contains(photo.StorageKey))
            {
                continue;
            }

            applicationDb.PrismMediaOutboxMessages.Add(new PrismMediaOutboxMessage
            {
                EventType = PrismMediaOutboxEventType.ActivityPhotoUpsert,
                ActivityId = photo.ActivityId,
                AttachmentId = photo.Id,
                StorageKey = photo.StorageKey,
                Reason = "Startup Activity photo backfill",
                OccurredAtUtc = now,
                AvailableAfterUtc = now
            });
            queued++;
        }

        if (queued > 0)
        {
            await applicationDb.SaveChangesAsync(cancellationToken);
        }

        return queued;
    }

    private async Task<int> DrainOnceAsync(CancellationToken cancellationToken)
    {
        var messages = await ClaimBatchAsync(cancellationToken);
        if (messages.Count > 0)
        {
            _runtimeState.MarkClaimed();
        }
        foreach (var message in messages)
        {
            await ProcessOneAsync(message, cancellationToken);
        }

        return messages.Count;
    }

    private async Task<IReadOnlyList<PrismMediaOutboxMessage>> ClaimBatchAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var recovered = await db.PrismMediaOutboxMessages
            .Where(message => message.Status == PrismMediaOutboxStatus.Processing
                              && message.LockExpiresAtUtc != null
                              && message.LockExpiresAtUtc < now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, PrismMediaOutboxStatus.Pending)
                .SetProperty(message => message.LockedBy, (string?)null)
                .SetProperty(message => message.LockExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(message => message.AvailableAfterUtc, now)
                .SetProperty(message => message.LastError,
                    "The previous worker lease expired; the event was recovered automatically."),
                cancellationToken);

        if (recovered > 0)
        {
            _logger.LogWarning("Recovered {Count} expired PRISM media outbox lease(s)", recovered);
        }

        List<PrismMediaOutboxMessage> claimed;
        if ((db.Database.ProviderName ?? string.Empty)
            .Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            claimed = await db.PrismMediaOutboxMessages
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""PrismMediaOutboxMessages""
                    WHERE ""Status"" = 'Pending'
                      AND ""AvailableAfterUtc"" <= {now}
                    ORDER BY ""AvailableAfterUtc"", ""Id""
                    FOR UPDATE SKIP LOCKED
                    LIMIT {BatchSize}")
                .ToListAsync(cancellationToken);
        }
        else
        {
            // Development/test fallback. Production PostgreSQL uses SKIP LOCKED above.
            claimed = await db.PrismMediaOutboxMessages
                .Where(message => message.Status == PrismMediaOutboxStatus.Pending
                                  && message.AvailableAfterUtc <= now)
                .OrderBy(message => message.AvailableAfterUtc)
                .ThenBy(message => message.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
        }

        foreach (var message in claimed)
        {
            message.Status = PrismMediaOutboxStatus.Processing;
            message.AttemptCount++;
            message.ProcessingStartedAtUtc = now;
            message.ProcessedAtUtc = null;
            message.LockedBy = _workerId;
            message.LockExpiresAtUtc = now.Add(LeaseDuration);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claimed.Select(Clone).ToArray();
    }

    private async Task ProcessOneAsync(
        PrismMediaOutboxMessage claimed,
        CancellationToken cancellationToken)
    {
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseTask = RenewLeaseLoopAsync(claimed.Id, leaseCancellation.Token);
        try
        {
            try
            {
                using (var processingScope = _scopeFactory.CreateScope())
                {
                    var ingestion = processingScope.ServiceProvider
                        .GetRequiredService<IPrismActivityMediaIngestionService>();
                    await ingestion.ProcessAsync(claimed, cancellationToken);
                }

                using var completionScope = _scopeFactory.CreateScope();
                var db = completionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var entity = await db.PrismMediaOutboxMessages.SingleOrDefaultAsync(
                    message => message.Id == claimed.Id
                               && message.Status == PrismMediaOutboxStatus.Processing
                               && message.LockedBy == _workerId,
                    cancellationToken);
                if (entity is null)
                {
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                entity.Status = PrismMediaOutboxStatus.Completed;
                entity.ProcessedAtUtc = now;
                entity.LockedBy = null;
                entity.LockExpiresAtUtc = null;
                entity.LastError = null;
                await db.SaveChangesAsync(cancellationToken);
                _runtimeState.MarkCompleted();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RecordFailureAsync(claimed, ex, cancellationToken);
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
                // Normal lease-renewal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Lease renewal for PRISM media outbox event {EventId} ended unexpectedly",
                    claimed.EventId);
            }
        }
    }

    private async Task RenewLeaseLoopAsync(long messageId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var renewed = await db.PrismMediaOutboxMessages
                .Where(message => message.Id == messageId
                                  && message.Status == PrismMediaOutboxStatus.Processing
                                  && message.LockedBy == _workerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.LockExpiresAtUtc,
                        DateTimeOffset.UtcNow.Add(LeaseDuration)),
                    cancellationToken);
            if (renewed == 0)
            {
                return;
            }
        }
    }

    private async Task RecordFailureAsync(
        PrismMediaOutboxMessage claimed,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entity = await db.PrismMediaOutboxMessages.SingleOrDefaultAsync(
                message => message.Id == claimed.Id
                           && message.Status == PrismMediaOutboxStatus.Processing
                           && message.LockedBy == _workerId,
                cancellationToken);
            if (entity is null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var exhausted = entity.AttemptCount >= entity.MaxAttempts;
            _runtimeState.MarkFailed(Trim(exception.GetBaseException().Message, 2048));
            entity.Status = exhausted
                ? PrismMediaOutboxStatus.DeadLetter
                : PrismMediaOutboxStatus.Pending;
            entity.AvailableAfterUtc = exhausted
                ? now
                : now.Add(GetRetryDelay(entity.AttemptCount));
            entity.LockedBy = null;
            entity.LockExpiresAtUtc = null;
            entity.LastError = Trim(exception.GetBaseException().Message, 2048);
            await db.SaveChangesAsync(cancellationToken);

            if (exhausted)
            {
                _logger.LogError(
                    exception,
                    "PRISM media outbox event {EventId} dead-lettered after {Attempts} attempts",
                    entity.EventId,
                    entity.AttemptCount);
            }
            else
            {
                _logger.LogWarning(
                    exception,
                    "PRISM media outbox event {EventId} failed; retry {Attempt}/{Maximum} is scheduled for {AvailableAfter}",
                    entity.EventId,
                    entity.AttemptCount,
                    entity.MaxAttempts,
                    entity.AvailableAfterUtc);
            }
        }
        catch (Exception persistenceException)
        {
            _logger.LogCritical(
                persistenceException,
                "Unable to persist failure state for PRISM media outbox event {EventId}",
                claimed.EventId);
        }
    }

    private async Task CleanupCompletedAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = DateTimeOffset.UtcNow.Subtract(CompletedRetention);
        await db.PrismMediaOutboxMessages
            .Where(message => message.Status == PrismMediaOutboxStatus.Completed
                              && message.ProcessedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 6);
        var seconds = Math.Min(300, 5 * Math.Pow(2, exponent));
        return TimeSpan.FromSeconds(seconds);
    }

    private static PrismMediaOutboxMessage Clone(PrismMediaOutboxMessage message)
        => new()
        {
            Id = message.Id,
            EventId = message.EventId,
            EventType = message.EventType,
            Status = message.Status,
            ActivityId = message.ActivityId,
            AttachmentId = message.AttachmentId,
            StorageKey = message.StorageKey,
            Reason = message.Reason,
            AttemptCount = message.AttemptCount,
            MaxAttempts = message.MaxAttempts,
            OccurredAtUtc = message.OccurredAtUtc,
            AvailableAfterUtc = message.AvailableAfterUtc,
            ProcessingStartedAtUtc = message.ProcessingStartedAtUtc,
            ProcessedAtUtc = message.ProcessedAtUtc,
            LockedBy = message.LockedBy,
            LockExpiresAtUtc = message.LockExpiresAtUtc,
            LastError = message.LastError
        };

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
