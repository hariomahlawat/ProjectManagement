using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IHttpContextAccessor _http;
        private readonly IAuditService _audit;
        private readonly IRoleNotificationService _roleNotifications;
        private readonly ILogger<UserManagementService> _logger;
        private readonly IAdminAuditService? _adminAudit;

        public UserManagementService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IHttpContextAccessor httpContextAccessor,
            IAuditService audit,
            IRoleNotificationService roleNotifications,
            ILogger<UserManagementService>? logger = null,
            IAdminAuditService? adminAudit = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _roleManager = roleManager ?? throw new ArgumentNullException(nameof(roleManager));
            _http = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _roleNotifications = roleNotifications ?? throw new ArgumentNullException(nameof(roleNotifications));
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UserManagementService>.Instance;
            _adminAudit = adminAudit;
        }

        private static bool IsAdministrativelyActive(ApplicationUser user) =>
            !user.IsDisabled && !user.PendingDeletion;

        private async Task<int> CountActiveAdminsAsync()
        {
            var admins = await _userManager.GetUsersInRoleAsync(RoleNames.Admin);
            return admins.Count(IsAdministrativelyActive);
        }

        public async Task<IList<ApplicationUser>> GetUsersAsync() =>
            await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();

        public Task<ApplicationUser?> GetUserByIdAsync(string userId) =>
            _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId)!;

        public async Task<IList<string>> GetRolesAsync() =>
            await _roleManager.Roles
                .Where(r => r.Name != null)
                .Select(r => r.Name!)
                .OrderBy(n => n)
                .ToListAsync();

        public async Task<IList<string>> GetUserRolesAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            return user is null ? new List<string>() : await _userManager.GetRolesAsync(user);
        }

        public async Task<IdentityResult> CreateUserAsync(
            string userName,
            string password,
            string fullName,
            string rank,
            IEnumerable<string> roles)
        {
            var roleResolution = await ResolveRolesAsync(roles);
            if (!roleResolution.Result.Succeeded)
            {
                return roleResolution.Result;
            }

            var user = new ApplicationUser
            {
                UserName = userName?.Trim(),
                MustChangePassword = true,
                FullName = fullName?.Trim() ?? string.Empty,
                Rank = rank?.Trim() ?? string.Empty
            };

            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database);
            var created = false;

            try
            {
                var createResult = await _userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    return createResult;
                }

                created = true;

                if (roleResolution.Roles.Count > 0)
                {
                    var addRolesResult = await _userManager.AddToRolesAsync(user, roleResolution.Roles);
                    if (!addRolesResult.Succeeded)
                    {
                        await RollBackCreationAsync(user, created, transaction);
                        return addRolesResult;
                    }
                }

                var stampResult = await _userManager.UpdateSecurityStampAsync(user);
                if (!stampResult.Succeeded)
                {
                    await RollBackCreationAsync(user, created, transaction);
                    return stampResult;
                }

                await RecordAuditAsync(
                    "AdminUserCreated",
                    user,
                    before: null,
                    after: new { user.FullName, user.Rank, Roles = roleResolution.Roles });

                await transaction.CommitAsync();
                return IdentityResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Atomic user creation failed for {UserName}.", userName);
                await RollBackCreationAsync(user, created, transaction);
                return Failure("The user could not be created. No partial account was retained.");
            }
        }

        public async Task<IdentityResult> UpdateUserAsync(
            string userId,
            string fullName,
            string rank,
            IEnumerable<string> roles)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return Failure("User not found.");
            }

            if (user.PendingDeletion)
            {
                return Failure("Undo the deletion request before editing this account.");
            }

            var roleResolution = await ResolveRolesAsync(roles);
            if (!roleResolution.Result.Succeeded)
            {
                return roleResolution.Result;
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var targetRoles = new HashSet<string>(roleResolution.Roles, StringComparer.OrdinalIgnoreCase);
            var toRemove = currentRoles.Where(role => !targetRoles.Contains(role)).ToArray();
            var toAdd = targetRoles
                .Where(role => !currentRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var previousFullName = user.FullName;
            var previousRank = user.Rank;
            var previousRoles = currentRoles.ToArray();

            await using var transaction = await RelationalTransactionScope.CreateAsync(
                _db.Database,
                IsolationLevel.Serializable);

            try
            {
                if (toRemove.Contains(RoleNames.Admin, StringComparer.OrdinalIgnoreCase)
                    && IsAdministrativelyActive(user)
                    && await CountActiveAdminsAsync() <= 1)
                {
                    await transaction.RollbackAsync();
                    return Failure("Cannot remove the Admin role from the last active Admin.");
                }

                user.FullName = fullName?.Trim() ?? string.Empty;
                user.Rank = rank?.Trim() ?? string.Empty;

                var detailsResult = await _userManager.UpdateAsync(user);
                if (!detailsResult.Succeeded)
                {
                    await RestoreUserStateAsync(user, previousFullName, previousRank, previousRoles, transaction);
                    return detailsResult;
                }

                if (toRemove.Length > 0)
                {
                    var removeResult = await _userManager.RemoveFromRolesAsync(user, toRemove);
                    if (!removeResult.Succeeded)
                    {
                        await RestoreUserStateAsync(user, previousFullName, previousRank, previousRoles, transaction);
                        return removeResult;
                    }
                }

                if (toAdd.Length > 0)
                {
                    var addResult = await _userManager.AddToRolesAsync(user, toAdd);
                    if (!addResult.Succeeded)
                    {
                        await RestoreUserStateAsync(user, previousFullName, previousRank, previousRoles, transaction);
                        return addResult;
                    }
                }

                var stampResult = await _userManager.UpdateSecurityStampAsync(user);
                if (!stampResult.Succeeded)
                {
                    await RestoreUserStateAsync(user, previousFullName, previousRank, previousRoles, transaction);
                    return stampResult;
                }

                await RecordAuditAsync(
                    "AdminUserUpdated",
                    user,
                    before: new { FullName = previousFullName, Rank = previousRank, Roles = previousRoles },
                    after: new { user.FullName, user.Rank, Roles = targetRoles.OrderBy(role => role).ToArray() });

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Atomic user update failed for {UserId}.", userId);
                await RestoreUserStateAsync(user, previousFullName, previousRank, previousRoles, transaction);
                return Failure("The user could not be updated. Previous details and roles were retained.");
            }

            if (toAdd.Length > 0 || toRemove.Length > 0)
            {
                var actorUserId = GetCurrentActorUserId() ?? user.Id;
                try
                {
                    await _roleNotifications.NotifyRolesUpdatedAsync(
                        user,
                        toAdd,
                        toRemove,
                        actorUserId,
                        CancellationToken.None);
                }
                catch (Exception notificationError)
                {
                    _logger.LogWarning(
                        notificationError,
                        "User {UserId} was updated, but role-change notifications could not be delivered.",
                        user.Id);
                }
            }

            return IdentityResult.Success;
        }

        public async Task<IdentityResult> UpdateUserDetailsAsync(string userId, string fullName, string rank)
        {
            var roles = await GetUserRolesAsync(userId);
            return await UpdateUserAsync(userId, fullName, rank, roles);
        }

        public async Task<IdentityResult> UpdateUserRolesAsync(string userId, IEnumerable<string> roles)
        {
            var user = await _userManager.FindByIdAsync(userId);
            return user is null
                ? Failure("User not found.")
                : await UpdateUserAsync(userId, user.FullName, user.Rank, roles);
        }

        public async Task<IdentityResult> ResetPasswordAsync(string userId, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return Failure("User not found.");
            }

            if (user.PendingDeletion)
            {
                return Failure("Undo the deletion request before resetting this account's password.");
            }

            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database);

            try
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, token, newPassword);
                if (!resetResult.Succeeded)
                {
                    return resetResult;
                }

                user.MustChangePassword = true;
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return updateResult;
                }

                var stampResult = await _userManager.UpdateSecurityStampAsync(user);
                if (!stampResult.Succeeded)
                {
                    await transaction.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return stampResult;
                }

                await RecordAuditAsync("AdminUserPasswordReset", user, before: null, after: new { user.MustChangePassword });
                await transaction.CommitAsync();
                return IdentityResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password reset failed for user {UserId}.", userId);
                await transaction.RollbackAsync();
                return Failure("The password could not be reset.");
            }
        }

        public async Task<IdentityResult> DeleteUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return Failure("User not found.");
            }

            var currentUserName = _http.HttpContext?.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(currentUserName)
                && string.Equals(currentUserName, user.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return Failure("You cannot delete your own account.");
            }

            await using var transaction = await RelationalTransactionScope.CreateAsync(
                _db.Database,
                IsolationLevel.Serializable);
            try
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains(RoleNames.Admin, StringComparer.OrdinalIgnoreCase)
                    && IsAdministrativelyActive(user)
                    && await CountActiveAdminsAsync() <= 1)
                {
                    await transaction.RollbackAsync();
                    return Failure("Cannot delete the last active Admin.");
                }

                var result = await _userManager.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return result;
                }

                await RecordAuditAsync("AdminUserDeleted", user, before: new { user.FullName, user.Rank, Roles = roles }, after: null);
                await transaction.CommitAsync();
                return IdentityResult.Success;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "Atomic user deletion failed for {UserId}.", userId);
                return Failure("The user could not be deleted.");
            }
        }

        private Task RecordAuditAsync(
            string action,
            ApplicationUser user,
            object? before,
            object? after,
            string? reason = null)
        {
            if (_adminAudit is not null)
            {
                return _adminAudit.RecordAsync(new AdminAuditEntry(
                    action,
                    "ApplicationUser",
                    user.Id,
                    before,
                    after,
                    reason,
                    Message: user.UserName,
                    ActorUserId: GetCurrentActorUserId()));
            }

            return _audit.LogAsync(action, userId: user.Id, userName: user.UserName);
        }

        private async Task<(IdentityResult Result, IReadOnlyList<string> Roles)> ResolveRolesAsync(IEnumerable<string>? requestedRoles)
        {
            var requested = (requestedRoles ?? Enumerable.Empty<string>())
                .Select(role => role?.Trim())
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (requested.Length == 0)
            {
                return (Failure("Assign at least one role to the user."), Array.Empty<string>());
            }

            var available = await _roleManager.Roles
                .Where(role => role.Name != null)
                .Select(role => role.Name!)
                .ToListAsync();

            var roleMap = available.ToDictionary(role => role, StringComparer.OrdinalIgnoreCase);
            var unknown = requested.Where(role => !roleMap.ContainsKey(role!)).ToArray();
            if (unknown.Length > 0)
            {
                return (Failure($"Unknown role selection: {string.Join(", ", unknown)}."), Array.Empty<string>());
            }

            var canonical = requested.Select(role => roleMap[role!]).OrderBy(role => role).ToArray();
            return (IdentityResult.Success, canonical);
        }

        private async Task RollBackCreationAsync(
            ApplicationUser user,
            bool created,
            RelationalTransactionScope transaction)
        {
            await transaction.RollbackAsync();

            if (!created)
            {
                return;
            }

            if (_db.Database.IsRelational())
            {
                _db.ChangeTracker.Clear();
                return;
            }

            try
            {
                var persisted = await _userManager.FindByIdAsync(user.Id);
                if (persisted is not null)
                {
                    await _userManager.DeleteAsync(persisted);
                }
            }
            catch (Exception compensationError)
            {
                _logger.LogCritical(
                    compensationError,
                    "Failed to remove partially created non-relational test account {UserId}.",
                    user.Id);
            }
        }

        private async Task RestoreUserStateAsync(
            ApplicationUser user,
            string previousFullName,
            string previousRank,
            IReadOnlyCollection<string> previousRoles,
            RelationalTransactionScope transaction)
        {
            await transaction.RollbackAsync();

            if (_db.Database.IsRelational())
            {
                _db.ChangeTracker.Clear();
                return;
            }

            try
            {
                var current = await _userManager.FindByIdAsync(user.Id);
                if (current is null)
                {
                    return;
                }

                current.FullName = previousFullName;
                current.Rank = previousRank;
                await _userManager.UpdateAsync(current);

                var currentRoles = await _userManager.GetRolesAsync(current);
                var remove = currentRoles.Except(previousRoles, StringComparer.OrdinalIgnoreCase).ToArray();
                var add = previousRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();

                if (remove.Length > 0)
                {
                    await _userManager.RemoveFromRolesAsync(current, remove);
                }

                if (add.Length > 0)
                {
                    await _userManager.AddToRolesAsync(current, add);
                }
            }
            catch (Exception compensationError)
            {
                _logger.LogCritical(
                    compensationError,
                    "Failed to restore non-relational user state for {UserId}.",
                    user.Id);
            }
        }

        private string? GetCurrentActorUserId() =>
            _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        private static IdentityResult Failure(string description) =>
            IdentityResult.Failed(new IdentityError { Description = description });
    }
}
