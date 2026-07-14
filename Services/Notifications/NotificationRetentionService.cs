using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationRetentionService : BackgroundService
{
    private const string WorkerKey = "notification-retention";
    private static readonly TimeSpan DefaultIdleDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<NotificationRetentionOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<NotificationRetentionService> _logger;
    private readonly IAdminWorkerStatusRegistry? _status;

    public NotificationRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationRetentionOptions> options,
        IClock clock,
        ILogger<NotificationRetentionService> logger,
        IAdminWorkerStatusRegistry? status = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _status = status;
        _status?.Register(WorkerKey, "Notification retention", _options.Value.GetSweepIntervalOrDefault());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.Value;
            var delay = options.GetSweepIntervalOrDefault();

            try
            {
                if (options.IsRetentionEnabled())
                {
                    _status?.MarkStarted(WorkerKey);
                    var removed = await RunOnceAsync(stoppingToken);
                    _status?.MarkSucceeded(WorkerKey, $"Removed {removed} notification record(s).");
                    if (removed > 0)
                    {
                        _logger.LogInformation("Notification retention removed {RemovedCount} notification records.", removed);
                    }
                }
                else
                {
                    _status?.MarkSucceeded(WorkerKey, "Retention is disabled by configuration.");
                }
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
                _status?.MarkFailed(WorkerKey, ex);
                _logger.LogError(ex, "Notification retention service failed to enforce retention policies.");
                delay = DefaultIdleDelay;
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.IsRetentionEnabled())
        {
            return 0;
        }
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var nowUtc = _clock.UtcNow.UtcDateTime;
        var removals = new List<Notification>();
        var removalIds = new HashSet<int>();

        if (options.MaxAge is TimeSpan maxAge && maxAge > TimeSpan.Zero)
        {
            var cutoff = nowUtc - maxAge;
            var stale = await db.Notifications
                .Where(n => n.CreatedUtc < cutoff)
                .ToListAsync(cancellationToken);

            foreach (var notification in stale)
            {
                if (removalIds.Add(notification.Id))
                {
                    removals.Add(notification);
                }
            }
        }

        if (options.MaxPerUser is int maxPerUser && maxPerUser > 0)
        {
            var overflowingUsers = await db.Notifications
                .Where(n => !string.IsNullOrEmpty(n.RecipientUserId))
                .GroupBy(n => n.RecipientUserId)
                .Select(g => new { UserId = g.Key!, Count = g.Count() })
                .Where(x => x.Count > maxPerUser)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);

            foreach (var userId in overflowingUsers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var extras = await db.Notifications
                    .Where(n => n.RecipientUserId == userId)
                    .OrderByDescending(n => n.CreatedUtc)
                    .ThenByDescending(n => n.Id)
                    .Skip(maxPerUser)
                    .ToListAsync(cancellationToken);

                foreach (var notification in extras)
                {
                    if (removalIds.Add(notification.Id))
                    {
                        removals.Add(notification);
                    }
                }
            }
        }

        var removedCount = removals.Count;
        if (removals.Count > 0)
        {
            db.Notifications.RemoveRange(removals);
        }

        if (options.CompletedDispatchMaxAge is TimeSpan completedDispatchMaxAge
            && completedDispatchMaxAge > TimeSpan.Zero)
        {
            var completedCutoff = nowUtc - completedDispatchMaxAge;
            var completedDispatches = await db.NotificationDispatches
                .Where(dispatch =>
                    dispatch.DispatchedUtc != null
                    && dispatch.DispatchedUtc < completedCutoff)
                .ToListAsync(cancellationToken);

            if (completedDispatches.Count > 0)
            {
                db.NotificationDispatches.RemoveRange(completedDispatches);
                removedCount += completedDispatches.Count;
            }
        }

        if (options.DeadLetterMaxAge is TimeSpan deadLetterMaxAge
            && deadLetterMaxAge > TimeSpan.Zero)
        {
            var deadLetterCutoff = nowUtc - deadLetterMaxAge;
            var deadLetters = await db.NotificationDispatches
                .Where(dispatch =>
                    dispatch.DeadLetteredUtc != null
                    && dispatch.DeadLetteredUtc < deadLetterCutoff)
                .ToListAsync(cancellationToken);

            if (deadLetters.Count > 0)
            {
                db.NotificationDispatches.RemoveRange(deadLetters);
                removedCount += deadLetters.Count;
            }
        }

        if (removedCount == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(cancellationToken);
        return removedCount;
    }
}
