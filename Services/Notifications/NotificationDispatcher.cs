using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationDispatcher : BackgroundService
{
    private const int BatchSize = 20;
    private const int ErrorMessageMaxLength = 2000;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<NotificationDispatcher> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedAny = false;

            try
            {
                processedAny = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification dispatcher encountered an error while processing a batch.");
                processedAny = true; // force error backoff below
                await DelayAsync(ErrorDelay, stoppingToken);
            }

            if (!processedAny)
            {
                await DelayAsync(IdleDelay, stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var preferenceService = scope.ServiceProvider.GetRequiredService<INotificationPreferenceService>();
        var deliveryService = scope.ServiceProvider.GetRequiredService<INotificationDeliveryService>();

        var now = _clock.UtcNow.UtcDateTime;

        var dispatches = await db.NotificationDispatches
            .Where(d => d.DispatchedUtc == null)
            .Where(d => d.LockedUntilUtc == null || d.LockedUntilUtc <= now)
            .OrderBy(d => d.CreatedUtc)
            .ThenBy(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(stoppingToken);

        if (dispatches.Count == 0)
        {
            return false;
        }

        var lockExpiry = now.Add(LockDuration);
        foreach (var dispatch in dispatches)
        {
            dispatch.LockedUntilUtc = lockExpiry;
            dispatch.AttemptCount += 1;
        }

        await db.SaveChangesAsync(stoppingToken);

        var notificationsToDeliver = new List<Notification>();

        foreach (var dispatch in dispatches)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                var allowed = await preferenceService.AllowsAsync(
                    dispatch.Kind,
                    dispatch.RecipientUserId,
                    dispatch.ProjectId,
                    stoppingToken);

                if (!allowed)
                {
                    dispatch.DispatchedUtc = _clock.UtcNow.UtcDateTime;
                    dispatch.LockedUntilUtc = null;
                    dispatch.Error = null;
                    continue;
                }

                Notification? existing = null;
                if (!string.IsNullOrWhiteSpace(dispatch.Fingerprint))
                {
                    existing = db.Notifications.Local.FirstOrDefault(n =>
                        string.Equals(n.RecipientUserId, dispatch.RecipientUserId, StringComparison.Ordinal) &&
                        string.Equals(n.Fingerprint, dispatch.Fingerprint, StringComparison.Ordinal));

                    if (existing is null)
                    {
                        existing = await db.Notifications
                            .Where(n => n.RecipientUserId == dispatch.RecipientUserId)
                            .Where(n => n.Fingerprint == dispatch.Fingerprint)
                            .FirstOrDefaultAsync(stoppingToken);
                    }
                }

                if (existing is not null)
                {
                    dispatch.DispatchedUtc = _clock.UtcNow.UtcDateTime;
                    dispatch.LockedUntilUtc = null;
                    dispatch.Error = null;
                    continue;
                }

                var dispatchedAt = _clock.UtcNow.UtcDateTime;

                var notification = new Notification
                {
                    RecipientUserId = dispatch.RecipientUserId,
                    Module = dispatch.Module,
                    EventType = dispatch.EventType,
                    ScopeType = dispatch.ScopeType,
                    ScopeId = dispatch.ScopeId,
                    ProjectId = dispatch.ProjectId,
                    ActorUserId = dispatch.ActorUserId,
                    Fingerprint = dispatch.Fingerprint,
                    Route = dispatch.Route,
                    Title = dispatch.Title,
                    Summary = dispatch.Summary,
                    CreatedUtc = dispatchedAt,
                    SourceDispatch = dispatch
                };

                db.Notifications.Add(notification);
                notificationsToDeliver.Add(notification);

                dispatch.DispatchedUtc = dispatchedAt;
                dispatch.LockedUntilUtc = null;
                dispatch.Error = null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var retryDelay = GetRetryDelay(dispatch.AttemptCount);
                var nextAttempt = _clock.UtcNow.UtcDateTime.Add(retryDelay);

                dispatch.LockedUntilUtc = nextAttempt;
                dispatch.DispatchedUtc = null;
                dispatch.Error = Truncate(ex.Message, ErrorMessageMaxLength);

                _logger.LogError(
                    ex,
                    "Failed to dispatch notification {DispatchId} on attempt {AttemptCount}.",
                    dispatch.Id,
                    dispatch.AttemptCount);
            }
        }

        await db.SaveChangesAsync(stoppingToken);

        if (notificationsToDeliver.Count > 0)
        {
            await deliveryService.DeliverAsync(notificationsToDeliver, stoppingToken);
        }

        return true;
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Swallow cancellation to allow graceful shutdown.
        }
    }

    private static TimeSpan GetRetryDelay(int attemptCount)
        => attemptCount switch
        {
            <= 1 => TimeSpan.FromSeconds(5),
            2 => TimeSpan.FromSeconds(15),
            3 => TimeSpan.FromMinutes(1),
            4 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(15)
        };

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
