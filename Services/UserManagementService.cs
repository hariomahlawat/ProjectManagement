using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UserManagementService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
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
            user.LockoutEnd = isActive ? null : DateTimeOffset.MaxValue;
            user.LockoutEnabled = !isActive;
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
            return await _userManager.DeleteAsync(user);
        }
    }
}
