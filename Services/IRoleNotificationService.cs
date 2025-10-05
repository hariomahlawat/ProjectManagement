using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services;

public interface IRoleNotificationService
{
    Task NotifyRolesUpdatedAsync(
        ApplicationUser user,
        IReadOnlyCollection<string> addedRoles,
        IReadOnlyCollection<string> removedRoles,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
