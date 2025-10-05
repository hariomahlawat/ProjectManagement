using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models.Notifications;

namespace ProjectManagement.Services.Notifications;

public interface INotificationPreferenceService
{
    Task<bool> AllowsAsync(
        NotificationKind kind,
        string userId,
        int? projectId = null,
        CancellationToken cancellationToken = default);
}
