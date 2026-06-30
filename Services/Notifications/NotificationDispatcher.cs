using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Hubs;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationDispatcher : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaximumAttempts = 8;
    private const int ErrorMessageMaxLength = 2000;

    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification dispatcher encountered an error while processing a batch.");
                await DelayAsync(ErrorDelay, stoppingToken);
                continue;
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
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationsHub, INotificationsClient>>();
        var notificationService = scope.ServiceProvider.GetRequiredService<UserNotificationService>();
        var principalFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var dispatches = await ClaimBatchAsync(db, stoppingToken);
        if (dispatches.Count == 0)
        {
            return false;
        }

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
                    CompleteDispatch(dispatch, _clock.UtcNow.UtcDateTime);
                    await db.SaveChangesAsync(stoppingToken);
                    continue;
                }

                var existing = await FindExistingNotificationAsync(db, dispatch, stoppingToken);
                if (existing is not null)
                {
                    CompleteDispatch(dispatch, _clock.UtcNow.UtcDateTime);
                    await db.SaveChangesAsync(stoppingToken);
                    continue;
                }

                var deliveredUtc = _clock.UtcNow.UtcDateTime;
                var notification = new Notification
                {
                    RecipientUserId = dispatch.RecipientUserId,
                    Kind = dispatch.Kind,
                    Module = dispatch.Module,
                    EventType = dispatch.EventType,
                    ScopeType = dispatch.ScopeType,
                    ScopeId = dispatch.ScopeId,
                    ProjectId = dispatch.ProjectId,
                    ActorUserId = dispatch.ActorUserId,
                    Fingerprint = dispatch.Fingerprint,
                    Route = NotificationPublisher.NormalizeRouteSegments(dispatch.Route),
                    Title = dispatch.Title,
                    Summary = dispatch.Summary,
                    CreatedUtc = EnsureUtc(dispatch.CreatedUtc),
                    DeliveredUtc = deliveredUtc,
                    SourceDispatch = dispatch,
                };

                db.Notifications.Add(notification);
                CompleteDispatch(dispatch, deliveredUtc);

                try
                {
                    await db.SaveChangesAsync(stoppingToken);
                    notificationsToDeliver.Add(notification);
                }
                catch (DbUpdateException)
                {
                    // A second app instance may have materialised this durable dispatch, or an
                    // equivalent producer fingerprint, after our pre-check. Unique indexes on
                    // SourceDispatchId and recipient/fingerprint make the operation idempotent.
                    db.Entry(notification).State = EntityState.Detached;

                    var duplicate = await db.Notifications
                        .AsNoTracking()
                        .AnyAsync(n =>
                            n.SourceDispatchId == dispatch.Id
                            || (!string.IsNullOrWhiteSpace(dispatch.Fingerprint)
                                && n.RecipientUserId == dispatch.RecipientUserId
                                && n.Fingerprint == dispatch.Fingerprint),
                            stoppingToken);

                    if (!duplicate)
                    {
                        throw;
                    }

                    CompleteDispatch(dispatch, deliveredUtc);
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation(
                        "Suppressed duplicate notification dispatch {DispatchId} for recipient {RecipientUserId}.",
                        dispatch.Id,
                        dispatch.RecipientUserId);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RegisterFailureAsync(db, dispatch, ex, stoppingToken);
            }
        }

        if (notificationsToDeliver.Count > 0)
        {
            await deliveryService.DeliverAsync(notificationsToDeliver, stoppingToken);
            await BroadcastNewNotificationsAsync(
                notificationsToDeliver,
                notificationService,
                hubContext,
                userManager,
                principalFactory,
                stoppingToken);
        }

        return true;
    }

    private async Task<List<NotificationDispatch>> ClaimBatchAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow.UtcDateTime;
        var lockExpiry = now.Add(LockDuration);
        var claimToken = Guid.NewGuid().ToString("N");

        var candidateIds = await db.NotificationDispatches
            .AsNoTracking()
            .Where(d => d.DispatchedUtc == null && d.DeadLetteredUtc == null)
            .Where(d => d.LockedUntilUtc == null || d.LockedUntilUtc <= now)
            .OrderBy(d => d.CreatedUtc)
            .ThenBy(d => d.Id)
            .Select(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            return new List<NotificationDispatch>();
        }

        if (db.Database.IsRelational())
        {
            await db.NotificationDispatches
                .Where(d => candidateIds.Contains(d.Id))
                .Where(d => d.DispatchedUtc == null && d.DeadLetteredUtc == null)
                .Where(d => d.LockedUntilUtc == null || d.LockedUntilUtc <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(d => d.LockToken, claimToken)
                    .SetProperty(d => d.LockedUntilUtc, lockExpiry)
                    .SetProperty(d => d.AttemptCount, d => d.AttemptCount + 1),
                    cancellationToken);

            return await db.NotificationDispatches
                .Where(d => d.LockToken == claimToken)
                .OrderBy(d => d.CreatedUtc)
                .ThenBy(d => d.Id)
                .ToListAsync(cancellationToken);
        }

        // EF's in-memory provider does not support ExecuteUpdateAsync. This branch keeps the
        // service testable while production providers use the atomic compare-and-set claim.
        var inMemoryRows = await db.NotificationDispatches
            .Where(d => candidateIds.Contains(d.Id))
            .Where(d => d.DispatchedUtc == null && d.DeadLetteredUtc == null)
            .Where(d => d.LockedUntilUtc == null || d.LockedUntilUtc <= now)
            .OrderBy(d => d.CreatedUtc)
            .ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

        foreach (var row in inMemoryRows)
        {
            row.LockToken = claimToken;
            row.LockedUntilUtc = lockExpiry;
            row.AttemptCount += 1;
        }

        await db.SaveChangesAsync(cancellationToken);
        return inMemoryRows;
    }

    private static async Task<Notification?> FindExistingNotificationAsync(
        ApplicationDbContext db,
        NotificationDispatch dispatch,
        CancellationToken cancellationToken)
    {
        var local = db.Notifications.Local.FirstOrDefault(n =>
            n.SourceDispatchId == dispatch.Id
            || (!string.IsNullOrWhiteSpace(dispatch.Fingerprint)
                && string.Equals(n.RecipientUserId, dispatch.RecipientUserId, StringComparison.Ordinal)
                && string.Equals(n.Fingerprint, dispatch.Fingerprint, StringComparison.Ordinal)));

        if (local is not null)
        {
            return local;
        }

        return await db.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n =>
                n.SourceDispatchId == dispatch.Id
                || (!string.IsNullOrWhiteSpace(dispatch.Fingerprint)
                    && n.RecipientUserId == dispatch.RecipientUserId
                    && n.Fingerprint == dispatch.Fingerprint),
                cancellationToken);
    }

    private async Task RegisterFailureAsync(
        ApplicationDbContext db,
        NotificationDispatch dispatch,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow.UtcDateTime;
        dispatch.Error = Truncate(exception.Message, ErrorMessageMaxLength);
        dispatch.LockToken = null;

        if (dispatch.AttemptCount >= MaximumAttempts)
        {
            dispatch.DeadLetteredUtc = now;
            dispatch.LockedUntilUtc = null;

            _logger.LogError(
                exception,
                "Dead-lettered notification dispatch {DispatchId} after {AttemptCount} attempts.",
                dispatch.Id,
                dispatch.AttemptCount);
        }
        else
        {
            dispatch.LockedUntilUtc = now.Add(GetRetryDelay(dispatch.AttemptCount));

            _logger.LogWarning(
                exception,
                "Failed to dispatch notification {DispatchId} on attempt {AttemptCount}; it will be retried.",
                dispatch.Id,
                dispatch.AttemptCount);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void CompleteDispatch(NotificationDispatch dispatch, DateTime dispatchedUtc)
    {
        dispatch.DispatchedUtc = EnsureUtc(dispatchedUtc);
        dispatch.LockedUntilUtc = null;
        dispatch.LockToken = null;
        dispatch.Error = null;
    }

    private static async Task BroadcastNewNotificationsAsync(
        IReadOnlyCollection<Notification> notifications,
        UserNotificationService notificationService,
        IHubContext<NotificationsHub, INotificationsClient> hubContext,
        UserManager<ApplicationUser> userManager,
        IUserClaimsPrincipalFactory<ApplicationUser> principalFactory,
        CancellationToken cancellationToken)
    {
        foreach (var userGroup in notifications.GroupBy(n => n.RecipientUserId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var userId = userGroup.Key;
            if (string.IsNullOrWhiteSpace(userId))
            {
                continue;
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
            {
                continue;
            }

            var principal = await principalFactory.CreateAsync(user);
            var items = await notificationService.ProjectAsync(
                principal,
                userId,
                userGroup.ToList(),
                cancellationToken);

            foreach (var item in items)
            {
                await hubContext.Clients.User(userId).ReceiveNotification(item);
            }

            var unreadCount = await notificationService.CountUnreadAsync(
                principal,
                userId,
                cancellationToken);
            await hubContext.Clients.User(userId).ReceiveUnreadCount(unreadCount);
        }
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
    }

    private static TimeSpan GetRetryDelay(int attemptCount)
        => attemptCount switch
        {
            <= 1 => TimeSpan.FromSeconds(5),
            2 => TimeSpan.FromSeconds(15),
            3 => TimeSpan.FromMinutes(1),
            4 => TimeSpan.FromMinutes(5),
            5 => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromHours(1),
        };

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
