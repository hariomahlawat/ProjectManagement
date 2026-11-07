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
    public void Period_CountsIstCalendarDays()
    {
        // 23:50 IST on 7 Nov; the last sign-in was 08:00 IST on 9 Oct, 29 IST dates earlier but
        // under 29.7 elapsed days. A 30-day period starts on 9 Oct, so it includes it.
        var now = new DateTimeOffset(2026, 11, 7, 18, 20, 0, TimeSpan.Zero);
        Assert.Equal(30, DetailsModel.PeriodIncluding(new DateTime(2026, 10, 9, 2, 30, 0, DateTimeKind.Utc), now));

        // 00:05 IST on 8 Nov; the last sign-in was 00:10 IST on 9 Oct: under 30 elapsed days, but
        // 30 IST dates back, so the 30-day period (from 10 Oct) would miss it.
        now = new DateTimeOffset(2026, 11, 7, 18, 35, 0, TimeSpan.Zero);
        Assert.Equal(90, DetailsModel.PeriodIncluding(new DateTime(2026, 10, 8, 18, 40, 0, DateTimeKind.Utc), now));
    }

    [Fact]
    public void NoSignInYet_OpensTheDefaultPeriod()
    {
        Assert.Equal(30, DetailsModel.PeriodIncluding(null, Now));
    }
}
