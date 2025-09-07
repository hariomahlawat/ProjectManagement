using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IHttpContextAccessor _contextAccessor;

        public UserManagementService(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IHttpContextAccessor contextAccessor)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _contextAccessor = contextAccessor;
        }

        public async Task<IList<ApplicationUser>> GetUsersAsync()
        {
            return await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
        }

        public async Task<ApplicationUser?> GetUserByIdAsync(string userId)
        {
            return await _userManager.FindByIdAsync(userId);
        }

        public async Task<IList<string>> GetRolesAsync()
        {
            return await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
        }

        public async Task<IList<string>> GetUserRolesAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return new List<string>();
            return await _userManager.GetRolesAsync(user);
        }

        public async Task<IdentityResult> CreateUserAsync(string userName, string password, string role)
        {
            var user = new ApplicationUser { UserName = userName, MustChangePassword = true };
            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded) return result;
            if (!string.IsNullOrWhiteSpace(role))
                await _userManager.AddToRoleAsync(user, role);
            return result;
        }

        public async Task<IdentityResult> UpdateUserRoleAsync(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found" });
            var currentRoles = await _userManager.GetRolesAsync(user);
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded) return removeResult;
            return await _userManager.AddToRoleAsync(user, role);
        }

        public async Task ToggleUserActivationAsync(string userId, bool isActive)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;

            // Keep lockout mechanism active
            user.LockoutEnabled = true;
            user.LockoutEnd = isActive ? null : DateTimeOffset.MaxValue;

            await _userManager.UpdateAsync(user);
        }

        public async Task<IdentityResult> ResetPasswordAsync(string userId, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return IdentityResult.Failed(new IdentityError { Description = "User not found" });
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
            if (result.Succeeded)
            {
                user.MustChangePassword = true;
                await _userManager.UpdateAsync(user);
            }
            return result;
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
            var currentUser = _contextAccessor.HttpContext?.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(currentUser) &&
                string.Equals(currentUser, user.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return IdentityResult.Failed(new IdentityError { Description = "You cannot delete your own account." });
            }

            return await _userManager.DeleteAsync(user);
        }
    }
}
