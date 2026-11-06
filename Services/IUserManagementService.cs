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
            IEnumerable<string> roles,
            UserAccountKind accountKind = UserAccountKind.Human);

        Task<IdentityResult> UpdateUserAsync(
            string userId,
            string fullName,
            string rank,
            IEnumerable<string> roles,
            UserAccountKind? accountKind = null,
            string? expectedVersion = null);

        Task<IdentityResult> ResetPasswordAsync(string userId, string newPassword);

        /// <summary>Clears a temporary lockout (failed sign-ins) on an enabled account.</summary>
        Task<IdentityResult> UnlockAsync(string userId);
    }
}
