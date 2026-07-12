using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin;

public enum AdminUserAccountState
{
    Active = 0,
    MustChangePassword = 1,
    TemporarilyLocked = 2,
    Disabled = 3,
    PendingDeletion = 4
}

public sealed record AdminUserAccountStateInfo(
    AdminUserAccountState State,
    string DisplayName,
    string BadgeCssClass,
    bool CanSignIn,
    DateTimeOffset? LockoutEndUtc = null);

public interface IUserAccountStateResolver
{
    AdminUserAccountStateInfo Resolve(ApplicationUser user, DateTimeOffset nowUtc);
}

public sealed class UserAccountStateResolver : IUserAccountStateResolver
{
    public AdminUserAccountStateInfo Resolve(ApplicationUser user, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.PendingDeletion)
        {
            return new(
                AdminUserAccountState.PendingDeletion,
                "Pending deletion",
                "text-bg-danger",
                false);
        }

        if (user.IsDisabled)
        {
            return new(
                AdminUserAccountState.Disabled,
                "Disabled",
                "text-bg-secondary",
                false);
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > nowUtc)
        {
            return new(
                AdminUserAccountState.TemporarilyLocked,
                "Temporarily locked",
                "text-bg-warning",
                false,
                user.LockoutEnd);
        }

        if (user.MustChangePassword)
        {
            return new(
                AdminUserAccountState.MustChangePassword,
                "Password change required",
                "text-bg-info",
                true);
        }

        return new(
            AdminUserAccountState.Active,
            "Active",
            "text-bg-success",
            true);
    }
}
