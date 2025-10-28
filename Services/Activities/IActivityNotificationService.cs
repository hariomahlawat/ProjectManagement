using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Activities;

public interface IActivityNotificationService
{
    Task NotifyDeleteRequestedAsync(ActivityDeleteNotificationContext context, CancellationToken cancellationToken);

    Task NotifyDeleteApprovedAsync(ActivityDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken);

    Task NotifyDeleteRejectedAsync(ActivityDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken);
}
