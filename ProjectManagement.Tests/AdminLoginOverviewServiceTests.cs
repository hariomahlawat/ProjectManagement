using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminLoginOverviewServiceTests
{
    [Fact]
    public async Task GetAsync_GroupsAuthenticationEventsByIstCalendarDay()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "user1",
            FullName = "User One",
            MustChangePassword = false
        });
        db.AuthEvents.AddRange(
            new AuthEvent
            {
                Id = Guid.NewGuid(),
                UserId = "user-1",
                Event = AuthenticationEventNames.LoginSucceeded,
                // 11 Jul 2026, 23:45 IST
                WhenUtc = new DateTimeOffset(2026, 7, 11, 18, 15, 0, TimeSpan.Zero)
            },
            new AuthEvent
            {
                Id = Guid.NewGuid(),
                UserId = "user-1",
                Event = AuthenticationEventNames.LoginSucceeded,
                // 12 Jul 2026, 00:15 IST
                WhenUtc = new DateTimeOffset(2026, 7, 11, 18, 45, 0, TimeSpan.Zero)
            });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
        var time = new AdminTimeService(clock);
        var service = new AdminLoginOverviewService(db, time, new UserAccountStateResolver());

        var snapshot = await service.GetAsync();

        Assert.Equal(30, snapshot.LoginsPerDay.Count);
        Assert.Equal(1, snapshot.LoginsPerDay.Single(item => item.Date == new DateOnly(2026, 7, 11)).Count);
        Assert.Equal(1, snapshot.LoginsPerDay.Single(item => item.Date == new DateOnly(2026, 7, 12)).Count);
        Assert.Equal("User One", Assert.Single(snapshot.TopUsers).UserName);
        Assert.Equal(2, snapshot.TopUsers[0].LoginCount);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
