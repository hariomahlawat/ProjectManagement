using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class UserLifecycleService : IUserLifecycleService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _audit;
        private readonly UserLifecycleOptions _options;
        private readonly IClock _clock;
        private readonly ILogger<UserLifecycleService> _logger;

        public UserLifecycleService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IAuditService audit,
            IOptions<UserLifecycleOptions> options,
            IClock clock,
            ILogger<UserLifecycleService>? logger = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UserLifecycleService>.Instance;
        }

        private async Task<bool> IsLastActiveAdminAsync(ApplicationUser target)
        {
            if (!await _userManager.IsInRoleAsync(target, RoleNames.Admin))
            {
                return false;
            }

            var admins = await _userManager.GetUsersInRoleAsync(RoleNames.Admin);
            return admins.Count(admin =>
                admin.Id != target.Id
                && !admin.IsDisabled
                && !admin.PendingDeletion) == 0;
        }

        public async Task DisableAsync(string targetUserId, string actorUserId, string reason)
        {
            if (targetUserId == actorUserId)
            {
                throw new InvalidOperationException("You cannot disable your own account.");
            }

            var user = await FindRequiredUserAsync(targetUserId);
            if (user.PendingDeletion)
            {
                throw new InvalidOperationException("The account is pending deletion. Undo the deletion request before changing its status.");
            }

            if (user.IsDisabled)
            {
                return;
            }

            var before = CaptureState(user);
            await using var transaction = await RelationalTransactionScope.CreateAsync(
                _db.Database,
                IsolationLevel.Serializable);

            try
            {
                if (await IsLastActiveAdminAsync(user))
                {
                    await transaction.RollbackAsync();
                    throw new InvalidOperationException("Cannot disable the last active Admin.");
                }

                var now = _clock.UtcNow.UtcDateTime;
                user.IsDisabled = true;
                user.DisabledUtc = now;
                user.DisabledByUserId = actorUserId;
                user.LockoutEnabled = true;
                user.LockoutEnd = DateTimeOffset.MaxValue;

                await EnsureSucceededAsync(_userManager.UpdateAsync(user), "The account could not be disabled.");
                await EnsureSucceededAsync(_userManager.UpdateSecurityStampAsync(user), "The account security state could not be refreshed.");

                await _audit.LogAsync(
                    "AdminUserDisabled",
                    userId: user.Id,
                    userName: user.UserName,
                    data: new Dictionary<string, string?>
                    {
                        ["Reason"] = reason?.Trim(),
                        ["Actor"] = actorUserId
                    });

                await transaction.CommitAsync();
            }
            catch
            {
                await RestoreAfterFailureAsync(user, before, transaction);
                throw;
            }
        }

        public async Task EnableAsync(string targetUserId, string actorUserId)
        {
            var user = await FindRequiredUserAsync(targetUserId);
            if (user.PendingDeletion)
            {
                throw new InvalidOperationException("The account is pending deletion. Undo the deletion request before enabling it.");
            }

            if (!user.IsDisabled)
            {
                return;
            }

            var before = CaptureState(user);
            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database);

            try
            {
                user.IsDisabled = false;
                user.DisabledUtc = null;
                user.DisabledByUserId = null;
                user.LockoutEnd = null;
                user.LockoutEnabled = true;
                user.AccessFailedCount = 0;

                await EnsureSucceededAsync(_userManager.UpdateAsync(user), "The account could not be enabled.");
                await EnsureSucceededAsync(_userManager.UpdateSecurityStampAsync(user), "The account security state could not be refreshed.");

                await _audit.LogAsync(
                    "AdminUserEnabled",
                    userId: user.Id,
                    userName: user.UserName,
                    data: new Dictionary<string, string?> { ["Actor"] = actorUserId });

                await transaction.CommitAsync();
            }
            catch
            {
                await RestoreAfterFailureAsync(user, before, transaction);
                throw;
            }
        }

        public async Task<(bool Allowed, string? ReasonBlocked, DateTime? ScheduledPurgeUtc)> RequestHardDeleteAsync(
            string targetUserId,
            string actorUserId)
        {
            if (targetUserId == actorUserId)
            {
                return (false, "You cannot delete your own account.", null);
            }

            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user is null)
            {
                return (false, "User not found.", null);
            }

            if (user.PendingDeletion && user.DeletionRequestedUtc.HasValue)
            {
                return (
                    true,
                    null,
                    user.DeletionRequestedUtc.Value.AddMinutes(_options.UndoWindowMinutes));
            }

            var nowUtc = _clock.UtcNow.UtcDateTime;
            var ageHours = (nowUtc - DateTime.SpecifyKind(user.CreatedUtc, DateTimeKind.Utc)).TotalHours;
            if (ageHours > _options.HardDeleteWindowHours)
            {
                return (false, $"Account older than {_options.HardDeleteWindowHours}h. Use Disable instead.", null);
            }

            var before = CaptureState(user);
            await using var transaction = await RelationalTransactionScope.CreateAsync(
                _db.Database,
                IsolationLevel.Serializable);

            try
            {
                if (await IsLastActiveAdminAsync(user))
                {
                    await transaction.RollbackAsync();
                    return (false, "Cannot delete the last active Admin.", null);
                }

                user.DeletionPreviousStateJson = JsonSerializer.Serialize(before with { DeletionPreviousStateJson = null });
                user.PendingDeletion = true;
                user.DeletionRequestedUtc = nowUtc;
                user.DeletionRequestedByUserId = actorUserId;
                user.IsDisabled = true;
                user.DisabledUtc = nowUtc;
                user.DisabledByUserId = actorUserId;
                user.LockoutEnabled = true;
                user.LockoutEnd = DateTimeOffset.MaxValue;

                await EnsureSucceededAsync(_userManager.UpdateAsync(user), "The deletion request could not be recorded.");
                await EnsureSucceededAsync(_userManager.UpdateSecurityStampAsync(user), "The account security state could not be refreshed.");

                await _audit.LogAsync(
                    "AdminUserDeleteRequested",
                    userId: user.Id,
                    userName: user.UserName,
                    data: new Dictionary<string, string?>
                    {
                        ["Actor"] = actorUserId,
                        ["PreviousDisabled"] = before.IsDisabled ? "true" : "false",
                        ["PreviousLockoutEnd"] = before.LockoutEnd?.ToString("O")
                    });

                await transaction.CommitAsync();

                var scheduled = nowUtc.AddMinutes(_options.UndoWindowMinutes);
                return (true, null, scheduled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to request hard deletion for user {UserId}.", targetUserId);
                await RestoreAfterFailureAsync(user, before, transaction);
                return (false, "The deletion request could not be completed.", null);
            }
        }

        public async Task<bool> UndoHardDeleteAsync(string targetUserId, string actorUserId)
        {
            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user is null || !user.PendingDeletion || user.DeletionRequestedUtc is null)
            {
                return false;
            }

            var nowUtc = _clock.UtcNow.UtcDateTime;
            var due = user.DeletionRequestedUtc.Value.AddMinutes(_options.UndoWindowMinutes);
            if (nowUtc > due)
            {
                return false;
            }

            var deleteRequestState = CaptureState(user);
            var previous = DeserializePreviousState(user.DeletionPreviousStateJson);

            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database);

            try
            {
                ApplyState(user, previous);
                user.PendingDeletion = false;
                user.DeletionRequestedUtc = null;
                user.DeletionRequestedByUserId = null;
                user.DeletionPreviousStateJson = null;

                await EnsureSucceededAsync(_userManager.UpdateAsync(user), "The deletion request could not be undone.");
                await EnsureSucceededAsync(_userManager.UpdateSecurityStampAsync(user), "The account security state could not be refreshed.");

                await _audit.LogAsync(
                    "AdminUserDeleteUndone",
                    userId: user.Id,
                    userName: user.UserName,
                    data: new Dictionary<string, string?>
                    {
                        ["Actor"] = actorUserId,
                        ["RestoredDisabled"] = previous.IsDisabled ? "true" : "false",
                        ["RestoredLockoutEnd"] = previous.LockoutEnd?.ToString("O")
                    });

                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to undo hard deletion for user {UserId}.", targetUserId);
                await RestoreAfterFailureAsync(user, deleteRequestState, transaction);
                return false;
            }
        }

        public async Task<bool> PurgeIfDueAsync(string targetUserId)
        {
            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user is null || !user.PendingDeletion || user.DeletionRequestedUtc is null)
            {
                return false;
            }

            var due = user.DeletionRequestedUtc.Value.AddMinutes(_options.UndoWindowMinutes);
            if (_clock.UtcNow.UtcDateTime < due)
            {
                return false;
            }

            await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database);
            try
            {
                var result = await _userManager.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(
                        "Failed to purge user {UserId}: {Errors}",
                        targetUserId,
                        string.Join("; ", result.Errors.Select(error => error.Description)));
                    return false;
                }

                await _audit.LogAsync("AdminUserPurged", userId: user.Id, userName: user.UserName);
                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "Failed to purge user {UserId} atomically.", targetUserId);
                return false;
            }
        }

        private async Task<ApplicationUser> FindRequiredUserAsync(string userId) =>
            await _userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        private static AccountStateSnapshot CaptureState(ApplicationUser user) =>
            new(
                user.IsDisabled,
                user.DisabledUtc,
                user.DisabledByUserId,
                user.LockoutEnabled,
                user.LockoutEnd,
                user.AccessFailedCount,
                user.PendingDeletion,
                user.DeletionRequestedUtc,
                user.DeletionRequestedByUserId,
                user.DeletionPreviousStateJson);

        private AccountStateSnapshot DeserializePreviousState(string? json)
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var state = JsonSerializer.Deserialize<AccountStateSnapshot>(json);
                    if (state is not null)
                    {
                        return state;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid deletion-state snapshot encountered while undoing a user deletion request.");
                }
            }

            // Safe legacy fallback for deletion requests created before this hardening update.
            return new AccountStateSnapshot(
                IsDisabled: false,
                DisabledUtc: null,
                DisabledByUserId: null,
                LockoutEnabled: true,
                LockoutEnd: null,
                AccessFailedCount: 0,
                PendingDeletion: false,
                DeletionRequestedUtc: null,
                DeletionRequestedByUserId: null,
                DeletionPreviousStateJson: null);
        }

        private static void ApplyState(ApplicationUser user, AccountStateSnapshot state)
        {
            user.IsDisabled = state.IsDisabled;
            user.DisabledUtc = state.DisabledUtc;
            user.DisabledByUserId = state.DisabledByUserId;
            user.LockoutEnabled = state.LockoutEnabled;
            user.LockoutEnd = state.LockoutEnd;
            user.AccessFailedCount = state.AccessFailedCount;
            user.PendingDeletion = state.PendingDeletion;
            user.DeletionRequestedUtc = state.DeletionRequestedUtc;
            user.DeletionRequestedByUserId = state.DeletionRequestedByUserId;
            user.DeletionPreviousStateJson = state.DeletionPreviousStateJson;
        }

        private async Task RestoreAfterFailureAsync(
            ApplicationUser user,
            AccountStateSnapshot previous,
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

                ApplyState(current, previous);
                await _userManager.UpdateAsync(current);
            }
            catch (Exception compensationError)
            {
                _logger.LogCritical(
                    compensationError,
                    "Failed to restore non-relational account state for user {UserId}.",
                    user.Id);
            }
        }

        private static async Task EnsureSucceededAsync(Task<IdentityResult> operation, string message)
        {
            var result = await operation;
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"{message} {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }

        private sealed record AccountStateSnapshot(
            bool IsDisabled,
            DateTime? DisabledUtc,
            string? DisabledByUserId,
            bool LockoutEnabled,
            DateTimeOffset? LockoutEnd,
            int AccessFailedCount,
            bool PendingDeletion,
            DateTime? DeletionRequestedUtc,
            string? DeletionRequestedByUserId,
            string? DeletionPreviousStateJson);
    }
}
