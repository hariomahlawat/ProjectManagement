using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

public interface INotificationDeliveryService
{
    Task DeliverAsync(
        IReadOnlyCollection<Notification> notifications,
        CancellationToken cancellationToken = default);
}
