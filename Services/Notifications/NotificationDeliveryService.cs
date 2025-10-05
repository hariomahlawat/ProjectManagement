using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

public sealed class NotificationDeliveryService : INotificationDeliveryService
{
    private readonly ILogger<NotificationDeliveryService> _logger;

    public NotificationDeliveryService(ILogger<NotificationDeliveryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task DeliverAsync(
        IReadOnlyCollection<Notification> notifications,
        CancellationToken cancellationToken = default)
    {
        if (notifications is null)
        {
            throw new ArgumentNullException(nameof(notifications));
        }

        if (notifications.Count == 0)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Queued {NotificationCount} notifications for downstream delivery.",
            notifications.Count);

        return Task.CompletedTask;
    }
}
