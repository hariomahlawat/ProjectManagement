using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class UserAccountStateResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
    private readonly UserAccountStateResolver _resolver = new();

    [Fact]
    public void PendingDeletion_TakesPrecedenceOverDisabledAndLockout()
    {
        var user = new ApplicationUser
        {
            PendingDeletion = true,
            IsDisabled = true,
            LockoutEnd = DateTimeOffset.MaxValue,
            MustChangePassword = true
        };

        var state = _resolver.Resolve(user, Now);

        Assert.Equal(AdminUserAccountState.PendingDeletion, state.State);
        Assert.False(state.CanSignIn);
    }

    [Fact]
    public void TemporaryLockout_IsDistinctFromDisabled()
    {
        var user = new ApplicationUser
        {
            LockoutEnd = Now.AddMinutes(15),
            MustChangePassword = false
        };

        var state = _resolver.Resolve(user, Now);

        Assert.Equal(AdminUserAccountState.TemporarilyLocked, state.State);
        Assert.False(state.CanSignIn);
    }

    [Fact]
    public void PasswordChangeRequirement_RemainsSignInCapable()
    {
        var state = _resolver.Resolve(new ApplicationUser { MustChangePassword = true }, Now);

        Assert.Equal(AdminUserAccountState.MustChangePassword, state.State);
        Assert.True(state.CanSignIn);
    }
}
