using System;
using System.Collections.Generic;
using System.Linq;
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

        public UserManagementService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IHttpContextAccessor httpContextAccessor)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _http = httpContextAccessor;
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
        public async Task<IdentityResult> CreateUserAsync(string userName, string password, IEnumerable<string> roles)
        {
            var user = new ApplicationUser { UserName = userName, MustChangePassword = true };
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
            return result;
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
            return IdentityResult.Success;
        }

        public async Task ToggleUserActivationAsync(string userId, bool isActive)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;

            // Keep lockout mechanism active
            user.LockoutEnabled = true;
            user.LockoutEnd = isActive ? null : DateTimeOffset.MaxValue;

            await _userManager.UpdateAsync(user);
            await _userManager.UpdateSecurityStampAsync(user);
        }

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
            }
            return res;
        }

        public async Task<IdentityResult> DeleteUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found" });

            // Prevent deleting the last Admin
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin"))
            {
                var admins = await _userManager.GetUsersInRoleAsync("Admin");
                if (admins.Count <= 1)
                    return IdentityResult.Failed(new IdentityError { Description = "Cannot delete the only Admin user." });
            }

            // Prevent self-delete from the UI flow
            var currentUserName = _http.HttpContext?.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(currentUserName) &&
                string.Equals(currentUserName, user.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return IdentityResult.Failed(new IdentityError { Description = "You cannot delete your own account." });
            }

            return await _userManager.DeleteAsync(user);
        }
    }
}
