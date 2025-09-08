using System;
using System.Threading.Tasks;

namespace ProjectManagement.Services
{
    public interface IUserLifecycleService
    {
        Task DisableAsync(string targetUserId, string actorUserId, string reason);
        Task EnableAsync(string targetUserId, string actorUserId);
        Task<(bool Allowed, string? ReasonBlocked, DateTime? ScheduledPurgeUtc)> RequestHardDeleteAsync(string targetUserId, string actorUserId);
        Task<bool> UndoHardDeleteAsync(string targetUserId, string actorUserId);
        Task<bool> PurgeIfDueAsync(string targetUserId);
    }
}
