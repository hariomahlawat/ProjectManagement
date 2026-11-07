using System;
using System.Threading.Tasks;

namespace ProjectManagement.Services
{
    public interface IUserLifecycleService
    {
        Task DisableAsync(string targetUserId, string actorUserId, string reason);

        /// <summary>
        /// Disables the account and says what happened, so callers can choose their own words
        /// without reading exception text. Unexpected failures (a database error) still throw.
        /// </summary>
        Task<UserDisableOutcome> TryDisableAsync(string targetUserId, string actorUserId, string reason);
        Task EnableAsync(string targetUserId, string actorUserId);
        Task<(bool Allowed, string? ReasonBlocked, DateTime? ScheduledPurgeUtc)> RequestHardDeleteAsync(string targetUserId, string actorUserId);
        Task<bool> UndoHardDeleteAsync(string targetUserId, string actorUserId);
        Task<bool> PurgeIfDueAsync(string targetUserId);
    }

    /// <summary>What a request to disable an account did.</summary>
    public enum UserDisableOutcome
    {
        /// <summary>The account is now disabled.</summary>
        Disabled = 0,
        /// <summary>The account was already disabled; nothing changed.</summary>
        AlreadyDisabled = 1,
        /// <summary>The account no longer exists.</summary>
        NotFound = 2,
        /// <summary>An administrator tried to disable their own account.</summary>
        SelfDisable = 3,
        /// <summary>The account is waiting to be deleted; that request must be undone first.</summary>
        PendingDeletion = 4,
        /// <summary>The account is the last active Admin.</summary>
        LastActiveAdmin = 5
    }
}
