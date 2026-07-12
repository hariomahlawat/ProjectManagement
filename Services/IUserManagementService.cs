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

        Task<IdentityResult> CreateUserAsync(
            string userName,
            string password,
            string fullName,
            string rank,
            IEnumerable<string> roles);

        Task<IdentityResult> UpdateUserAsync(
            string userId,
            string fullName,
            string rank,
            IEnumerable<string> roles);

        // Retained for compatibility with existing callers. New UI flows should use UpdateUserAsync.
        Task<IdentityResult> UpdateUserRolesAsync(string userId, IEnumerable<string> roles);
        Task<IdentityResult> UpdateUserDetailsAsync(string userId, string fullName, string rank);
        Task<IdentityResult> ResetPasswordAsync(string userId, string newPassword);
        Task<IdentityResult> DeleteUserAsync(string userId);
    }
}
