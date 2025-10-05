using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IHttpContextAccessor _http;
        private readonly IAuditService _audit;
        private readonly IRoleNotificationService _roleNotifications;

        public UserManagementService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IHttpContextAccessor httpContextAccessor,
            IAuditService audit,
            IRoleNotificationService roleNotifications)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _http = httpContextAccessor;
            _audit = audit;
            _roleNotifications = roleNotifications;
        }

        private static bool IsActive(ApplicationUser user) =>
            !user.LockoutEnd.HasValue || user.LockoutEnd <= DateTimeOffset.UtcNow;

        private async Task<int> CountActiveAdminsAsync()
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            return admins.Count(IsActive);
        }

        public async Task<IList<ApplicationUser>> GetUsersAsync() =>
            await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();

        public Task<ApplicationUser?> GetUserByIdAsync(string userId) =>
            _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId)!;

        public async Task<IList<string>> GetRolesAsync() =>
            await _roleManager.Roles.Select(r => r.Name!)
                .OrderBy(n => n).ToListAsync();

        public async Task<IList<string>> GetUserRolesAsync(string userId)
        {
            var u = await _userManager.FindByIdAsync(userId);
            return u is null ? new List<string>() : await _userManager.GetRolesAsync(u);
        }

        // -------- multi-role aware create ----------
        public async Task<IdentityResult> CreateUserAsync(string userName, string password, string fullName, string rank, IEnumerable<string> roles)
        {
            var user = new ApplicationUser { UserName = userName, MustChangePassword = true, FullName = fullName, Rank = rank };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded) return result;

            var targetRoles = (roles ?? Enumerable.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (targetRoles.Length > 0)
            {
                var add = await _userManager.AddToRolesAsync(user, targetRoles);
                if (!add.Succeeded) return add;
            }

            // Important: invalidate any cached tokens/sessions after role change
            await _userManager.UpdateSecurityStampAsync(user);
            await _audit.LogAsync("AdminUserCreated", userId: user.Id, userName: user.UserName,
                data: new Dictionary<string, string?>
                {
                    ["Roles"] = string.Join(",", targetRoles),
                    ["FullName"] = fullName,
                    ["Rank"] = rank
                });
            return result;
        }

        public async Task<IdentityResult> UpdateUserDetailsAsync(string userId, string fullName, string rank)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return IdentityResult.Failed(new IdentityError { Description = "User not found." });

            user.FullName = fullName;
            user.Rank = rank;
            var res = await _userManager.UpdateAsync(user);
            if (res.Succeeded)
                await _audit.LogAsync("AdminUserDetailsUpdated", userId: user.Id, userName: user.UserName,
                    data: new Dictionary<string, string?> { ["FullName"] = fullName, ["Rank"] = rank });
            return res;
        }

        // -------- multi-role update ----------
        public async Task<IdentityResult> UpdateUserRolesAsync(string userId, IEnumerable<string> roles)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return IdentityResult.Failed(new IdentityError { Description = "User not found." });

            var current = await _userManager.GetRolesAsync(user);
            var target = new HashSet<string>(
                (roles ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(),
                StringComparer.OrdinalIgnoreCase);

            var toRemove = current.Where(r => !target.Contains(r)).ToArray();
            var toAdd    = target.Where(r => !current.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();

            if (toRemove.Contains("Admin", StringComparer.OrdinalIgnoreCase))
            {
                if (IsActive(user))
                {
                    var activeAdmins = await CountActiveAdminsAsync();
                    if (activeAdmins <= 1)
                        return IdentityResult.Failed(new IdentityError { Description = "Cannot remove the Admin role from the last active Admin." });
                }
            }

            if (toRemove.Length > 0)
            {
                var rr = await _userManager.RemoveFromRolesAsync(user, toRemove);
                if (!rr.Succeeded) return rr;
            }
            if (toAdd.Length > 0)
            {
                var ar = await _userManager.AddToRolesAsync(user, toAdd);
                if (!ar.Succeeded) return ar;
            }

            await _userManager.UpdateSecurityStampAsync(user);
            await _audit.LogAsync("AdminUserRolesUpdated", userId: user.Id, userName: user.UserName,
                data: new Dictionary<string, string?>
                {
                    ["Added"] = string.Join(",", toAdd),
                    ["Removed"] = string.Join(",", toRemove)
                });
            var actorUserId = GetCurrentActorUserId() ?? user.Id;
            await _roleNotifications.NotifyRolesUpdatedAsync(user, toAdd, toRemove, actorUserId, CancellationToken.None);
            return IdentityResult.Success;
        }

        private string? GetCurrentActorUserId()
            => _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);


        public async Task<IdentityResult> ResetPasswordAsync(string userId, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return IdentityResult.Failed(new IdentityError { Description = "User not found." });

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var res = await _userManager.ResetPasswordAsync(user, token, newPassword);
            if (res.Succeeded)
            {
                user.MustChangePassword = true;
                await _userManager.UpdateAsync(user);
                await _userManager.UpdateSecurityStampAsync(user);
                await _audit.LogAsync("AdminUserPasswordReset", userId: user.Id, userName: user.UserName);
            }
            return res;
        }

        public async Task<IdentityResult> DeleteUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found" });

            // Prevent deleting the last active Admin
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin") && IsActive(user))
            {
                var activeAdmins = await CountActiveAdminsAsync();
                if (activeAdmins <= 1)
                    return IdentityResult.Failed(new IdentityError { Description = "Cannot delete the last active Admin." });
            }

            // Prevent self-delete from the UI flow
            var currentUserName = _http.HttpContext?.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(currentUserName) &&
                string.Equals(currentUserName, user.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return IdentityResult.Failed(new IdentityError { Description = "You cannot delete your own account." });
            }

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
                await _audit.LogAsync("AdminUserDeleted", userId: user.Id, userName: user.UserName);
            return result;
        }
    }
}
