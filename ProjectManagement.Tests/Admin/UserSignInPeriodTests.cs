using ProjectManagement.Areas.Admin.Pages.Users;
using Xunit;

namespace ProjectManagement.Tests.Admin;

// "View all sign-ins" on a user's page opens a period that includes their last sign-in
// (docs/ux/admin-review-fixes.md, PR 10), so it never opens an empty list.
public sealed class UserSignInPeriodTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 6, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(29, 30)]
    [InlineData(30, 90)]
    [InlineData(144, 180)]
    [InlineData(200, 365)]
    [InlineData(900, 365)]
    public void Period_IncludesTheLastSignIn(int daysAgo, int expected)
    {
        var lastSignIn = Now.UtcDateTime.AddDays(-daysAgo);

        Assert.Equal(expected, DetailsModel.PeriodIncluding(lastSignIn, Now));
    }

    [Fact]
    public void NoSignInYet_OpensTheDefaultPeriod()
    {
        Assert.Equal(30, DetailsModel.PeriodIncluding(null, Now));
    }
}
