using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Services.Notifications;

namespace ProjectManagement.Hubs;

[Authorize]
public sealed class NotificationsHub : Hub<INotificationsClient>
{
    private readonly UserNotificationService _notifications;

    public NotificationsHub(UserNotificationService notifications)
    {
        _notifications = notifications;
    }

    public async Task RequestUnreadCount(CancellationToken cancellationToken = default)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("User identifier is not available.");
        }

        var count = await _notifications.CountUnreadAsync(Context.User ?? new ClaimsPrincipal(), userId, cancellationToken);
        await Clients.Caller.ReceiveUnreadCount(count);
    }

    public async Task RequestRecentNotifications(int limit = 20, CancellationToken cancellationToken = default)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException("User identifier is not available.");
        }

        var options = new NotificationListOptions
        {
            Limit = limit,
        };

        var notifications = await _notifications.ListAsync(Context.User ?? new ClaimsPrincipal(), userId, options, cancellationToken);
        await Clients.Caller.ReceiveNotifications(notifications);
    }
}

public interface INotificationsClient
{
    Task ReceiveUnreadCount(int count);

    Task ReceiveNotification(NotificationListItem notification);

    Task ReceiveNotifications(IReadOnlyList<NotificationListItem> notifications);
}
