using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// One rule for an active administrator (docs/ux/admin-review-2-fixes.md, PR 5).
public sealed class ActiveAdministratorRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void Active_MeansNotDisabledAndNotPendingDeletion(bool disabled, bool pendingDeletion, bool expected)
    {
        Assert.Equal(expected, ActiveAdministratorRule.IsActive(disabled, pendingDeletion));
        Assert.Equal(expected, ActiveAdministratorRule.IsActive(new ApplicationUser
        {
            IsDisabled = disabled,
            PendingDeletion = pendingDeletion
        }));
    }

    [Fact]
    public void ATemporarilyLockedAccount_IsStillActive_AndShownAsLocked()
    {
        var locked = new ApplicationUser { LockoutEnabled = true, LockoutEnd = Now.AddMinutes(15) };

        Assert.True(ActiveAdministratorRule.IsActive(locked));
        Assert.True(ActiveAdministratorRule.IsTemporarilyLocked(locked.LockoutEnd, Now));
    }

    [Fact]
    public void ALockoutThatHasEnded_IsNotALock()
    {
        Assert.False(ActiveAdministratorRule.IsTemporarilyLocked(null, Now));
        Assert.False(ActiveAdministratorRule.IsTemporarilyLocked(Now, Now));
        Assert.False(ActiveAdministratorRule.IsTemporarilyLocked(Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void ADisabledAccount_IsNotActive_EvenThoughItsLockoutIsPermanent()
    {
        var disabled = new ApplicationUser { IsDisabled = true, LockoutEnabled = true, LockoutEnd = DateTimeOffset.MaxValue };

        Assert.False(ActiveAdministratorRule.IsActive(disabled));
    }
}
