using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class UserLifecycleService : IUserLifecycleService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _audit;
        private readonly UserLifecycleOptions _options;

        public UserLifecycleService(UserManager<ApplicationUser> userManager,
            IAuditService audit,
            IOptions<UserLifecycleOptions> options)
        {
            _userManager = userManager;
            _audit = audit;
            _options = options.Value;
        }

        private async Task<bool> IsLastActiveAdminAsync(ApplicationUser target)
        {
            if (!await _userManager.IsInRoleAsync(target, "Admin"))
                return false;
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            return admins.Count(a => !a.IsDisabled && !a.PendingDeletion && a.Id != target.Id) == 0;
        }

        public async Task DisableAsync(string targetUserId, string actorUserId, string reason)
        {
            if (targetUserId == actorUserId)
                throw new InvalidOperationException("You cannot disable your own account.");

            var user = await _userManager.FindByIdAsync(targetUserId) ??
                throw new InvalidOperationException("User not found.");

            if (await IsLastActiveAdminAsync(user))
                throw new InvalidOperationException("Cannot disable the last active Admin.");

            if (user.IsDisabled)
                return;

            user.IsDisabled = true;
            user.DisabledUtc = DateTime.UtcNow;
            user.DisabledByUserId = actorUserId;
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;

            await _userManager.UpdateAsync(user);
            await _userManager.UpdateSecurityStampAsync(user);
            await _audit.LogAsync("AdminUserDisabled", userId: user.Id, userName: user.UserName,
                data: new Dictionary<string, string?> { ["Reason"] = reason, ["Actor"] = actorUserId });
        }

        public async Task<(bool Allowed, string? ReasonBlocked, DateTime? ScheduledPurgeUtc)> RequestHardDeleteAsync(string targetUserId, string actorUserId)
        {
            if (targetUserId == actorUserId)
                return (false, "You cannot delete your own account.", null);

            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user == null)
                return (false, "User not found.", null);

            if (await IsLastActiveAdminAsync(user))
                return (false, "Cannot delete the last active Admin.", null);

            var ageHours = (DateTime.UtcNow - user.CreatedUtc).TotalHours;
            if (ageHours > _options.HardDeleteWindowHours)
                return (false, $"Account older than {_options.HardDeleteWindowHours}h. Use Disable instead.", null);

            user.PendingDeletion = true;
            user.DeletionRequestedUtc = DateTime.UtcNow;
            user.DeletionRequestedByUserId = actorUserId;
            user.IsDisabled = true;
            user.DisabledUtc ??= DateTime.UtcNow;
            user.DisabledByUserId ??= actorUserId;
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;

            await _userManager.UpdateAsync(user);
            await _userManager.UpdateSecurityStampAsync(user);
            await _audit.LogAsync("AdminUserDeleteRequested", userId: user.Id, userName: user.UserName,
                data: new Dictionary<string, string?> { ["Actor"] = actorUserId });

            var scheduled = user.DeletionRequestedUtc.Value.AddMinutes(_options.UndoWindowMinutes);
            return (true, null, scheduled);
        }

        public async Task<bool> UndoHardDeleteAsync(string targetUserId, string actorUserId)
        {
            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user == null || !user.PendingDeletion || user.DeletionRequestedUtc == null)
                return false;

            var due = user.DeletionRequestedUtc.Value.AddMinutes(_options.UndoWindowMinutes);
            if (DateTime.UtcNow > due)
                return false;

            user.PendingDeletion = false;
            user.DeletionRequestedUtc = null;
            user.DeletionRequestedByUserId = null;
            await _userManager.UpdateAsync(user);
            await _userManager.UpdateSecurityStampAsync(user);
            await _audit.LogAsync("AdminUserDeleteUndone", userId: user.Id, userName: user.UserName,
                data: new Dictionary<string, string?> { ["Actor"] = actorUserId });
            return true;
        }

        public async Task<bool> PurgeIfDueAsync(string targetUserId)
        {
            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user == null || !user.PendingDeletion || user.DeletionRequestedUtc == null)
                return false;

            var due = user.DeletionRequestedUtc.Value.AddMinutes(_options.UndoWindowMinutes);
            if (DateTime.UtcNow < due)
                return false;

            var res = await _userManager.DeleteAsync(user);
            if (res.Succeeded)
            {
                await _audit.LogAsync("AdminUserPurged", userId: user.Id, userName: user.UserName);
                return true;
            }
            return false;
        }
    }
}
