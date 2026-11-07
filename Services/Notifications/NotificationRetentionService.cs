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

    private static readonly NotificationKind[] ActionRequiredKinds =
        NotificationPresentationCatalog.ActionRequiredKinds.ToArray();
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
                    var result = await RunSweepAsync(stoppingToken);
                    _status?.MarkSucceeded(
                        WorkerKey,
                        BackgroundJobMessages.NotificationsTidied(result.RemovedCount, result.AutoReadCount));
                    if (result.RemovedCount > 0)
                    {
                        _logger.LogInformation("Notification retention removed {RemovedCount} notification records.", result.RemovedCount);
                    }

                    if (result.AutoReadCount > 0)
                    {
                        _logger.LogInformation("Notification retention auto-marked {AutoReadCount} stale notifications as read.", result.AutoReadCount);
                    }
                }
                else
                {
                    _status?.MarkSwitchedOff(WorkerKey, "Notifications are kept until this is switched on");
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

    /// <summary>Returns the number of records removed; see <see cref="RunSweepAsync"/>.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
        => (await RunSweepAsync(cancellationToken)).RemovedCount;

    internal async Task<NotificationRetentionSweepResult> RunSweepAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.IsRetentionEnabled())
        {
            return NotificationRetentionSweepResult.None;
        }
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var nowUtc = _clock.UtcNow.UtcDateTime;

        // SECTION: Auto-read. Runs before deletion and changes read state only.
        var autoReadCount = await AutoMarkStaleAsReadAsync(db, options, nowUtc, cancellationToken);

        // Unread action-required rows younger than the action auto-read window are protected
        // from age and per-user deletion. Once auto-read marks them read they become eligible.
        var protectActionRequired = NotificationRetentionOptions.IsEnabled(options.AutoReadActionRequiredAfter);
        var actionKinds = ActionRequiredKinds;

        var removals = new List<Notification>();
        var removalIds = new HashSet<int>();

        if (options.MaxAge is TimeSpan maxAge && maxAge > TimeSpan.Zero)
        {
            var cutoff = nowUtc - maxAge;
            // Rows with a kind from a newer release are left for an instance that can read them.
            var stale = await db.Notifications
                .WhereKindIsReadable()
                .Where(n => n.CreatedUtc < cutoff)
                .Where(n => !protectActionRequired
                    || n.ReadUtc != null
                    || !n.Kind.HasValue
                    || !actionKinds.Contains(n.Kind.Value))
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
                    .WhereKindIsReadable()
                    .Where(n => n.RecipientUserId == userId)
                    .OrderByDescending(n => n.CreatedUtc)
                    .ThenByDescending(n => n.Id)
                    .Skip(maxPerUser)
                    .ToListAsync(cancellationToken);

                foreach (var notification in extras)
                {
                    if (protectActionRequired && IsUnreadActionRequired(notification))
                    {
                        continue;
                    }

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
                .WhereKindIsReadable()
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
                .WhereKindIsReadable()
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
            return new NotificationRetentionSweepResult(0, autoReadCount);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new NotificationRetentionSweepResult(removedCount, autoReadCount);
    }

    private static async Task<int> AutoMarkStaleAsReadAsync(
        ApplicationDbContext db,
        NotificationRetentionOptions options,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var actionKinds = ActionRequiredKinds;
        var total = 0;

        if (options.AutoReadInformationalAfter is TimeSpan informationalAfter
            && informationalAfter > TimeSpan.Zero)
        {
            var cutoff = nowUtc - informationalAfter;
            // A kind from a newer release would otherwise count as informational here and be
            // auto-read after the informational window even if that release marks it
            // action-required. Leave it for an instance that knows the kind.
            total += await MarkReadAsync(
                db,
                db.Notifications.WhereKindIsReadable().Where(n =>
                    n.ReadUtc == null
                    && n.CreatedUtc < cutoff
                    && (!n.Kind.HasValue || !actionKinds.Contains(n.Kind.Value))),
                nowUtc,
                cancellationToken);
        }

        if (options.AutoReadActionRequiredAfter is TimeSpan actionAfter
            && actionAfter > TimeSpan.Zero)
        {
            var cutoff = nowUtc - actionAfter;
            total += await MarkReadAsync(
                db,
                db.Notifications.WhereKindIsReadable().Where(n =>
                    n.ReadUtc == null
                    && n.CreatedUtc < cutoff
                    && n.Kind.HasValue
                    && actionKinds.Contains(n.Kind.Value)),
                nowUtc,
                cancellationToken);
        }

        return total;
    }

    private static async Task<int> MarkReadAsync(
        ApplicationDbContext db,
        IQueryable<Notification> query,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            // A set-based update: idempotent, so overlapping sweeps on several instances are safe.
            return await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.ReadUtc, nowUtc)
                    .SetProperty(n => n.SeenUtc, n => n.SeenUtc ?? nowUtc),
                cancellationToken);
        }

        // EF InMemory does not implement ExecuteUpdateAsync.
        var rows = await query.ToListAsync(cancellationToken);
        foreach (var notification in rows)
        {
            notification.ReadUtc = nowUtc;
            notification.SeenUtc ??= nowUtc;
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return rows.Count;
    }

    private static bool IsUnreadActionRequired(Notification notification)
        => notification.ReadUtc is null
           && notification.Kind is NotificationKind kind
           && ActionRequiredKinds.Contains(kind);
}

internal sealed record NotificationRetentionSweepResult(int RemovedCount, int AutoReadCount)
{
    public static NotificationRetentionSweepResult None { get; } = new(0, 0);
}
