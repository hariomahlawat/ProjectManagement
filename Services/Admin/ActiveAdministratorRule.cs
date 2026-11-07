using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin;

/// <summary>
/// The one rule for whether an account counts as an active administrator (or, more generally, an
/// active holder of a role) — docs/ux/admin-review-2-fixes.md, PR 5.
/// <para>
/// An account is active when it is not disabled and not pending deletion. The last-administrator
/// guards in <see cref="UserManagementService"/> and <see cref="UserLifecycleService"/> and the
/// "active" counts and findings in Who has which role all use this rule, so they always agree.
/// </para>
/// <para>
/// A temporary lockout (too many failed sign-ins) does <b>not</b> make an account inactive. The
/// lockout ends by itself, and an administrator can clear it, so a locked administrator can still
/// recover the system. Treating locked administrators as inactive would make the guards stricter in
/// exactly the moment recovery matters (for example, refusing to disable a compromised second admin
/// because the only other admin mistyped a password). Who has which role shows a lock separately as
/// its own finding instead.
/// </para>
/// </summary>
public static class ActiveAdministratorRule
{
    /// <summary>True when an account with these flags counts as active.</summary>
    public static bool IsActive(bool isDisabled, bool pendingDeletion) =>
        !isDisabled && !pendingDeletion;

    /// <summary>True when <paramref name="user"/> counts as active.</summary>
    public static bool IsActive(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return IsActive(user.IsDisabled, user.PendingDeletion);
    }

    /// <summary>
    /// True when the account is temporarily locked out at <paramref name="nowUtc"/>. This is shown
    /// on its own and never changes whether the account is active.
    /// </summary>
    public static bool IsTemporarilyLocked(DateTimeOffset? lockoutEnd, DateTimeOffset nowUtc) =>
        lockoutEnd.HasValue && lockoutEnd.Value > nowUtc;
}
