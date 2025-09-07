using Microsoft.AspNetCore.Identity;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public interface IUserManagementService
    {
        Task<IList<ApplicationUser>> GetUsersAsync();
        Task<ApplicationUser?> GetUserByIdAsync(string userId);
        Task<IList<string>> GetRolesAsync();
        Task<IList<string>> GetUserRolesAsync(string userId);
        Task<IdentityResult> CreateUserAsync(string userName, string password, string role);
        Task<IdentityResult> UpdateUserRoleAsync(string userId, string role);
        Task ToggleUserActivationAsync(string userId, bool isActive);
        Task<IdentityResult> ResetPasswordAsync(string userId, string newPassword);
        Task<IdentityResult> DeleteUserAsync(string userId);
    }
}
