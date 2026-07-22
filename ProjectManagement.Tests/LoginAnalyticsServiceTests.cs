using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Tests;

public sealed class LoginAnalyticsServiceTests
{
    [Fact]
    public async Task GetAsync_UsesCanonicalSuccessfulAuthEventsAndResolvesIdentity()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        var user = new ApplicationUser
        {
            Id = "officer-1",
            UserName = "officer",
            FullName = "Officer One"
        };
        db.Users.Add(user);
        db.AuthEvents.AddRange(
            new AuthEvent
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Event = AuthenticationEventNames.LoginSucceeded,
                WhenUtc = clock.UtcNow.AddHours(-2)
            },
            new AuthEvent
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Event = "LoginFailed",
                WhenUtc = clock.UtcNow.AddHours(-1)
            });
        await db.SaveChangesAsync();

        var service = new LoginAnalyticsService(db, clock);
        var result = await service.GetAsync(
            30,
            markWeekendOdd: false,
            TimeZoneInfo.Utc,
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(18));

        var point = Assert.Single(result.Points);
        Assert.Equal(user.Id, point.UserId);
        Assert.Equal("officer", point.LoginName);
        Assert.Equal("Officer One", point.DisplayName);
        Assert.Equal(clock.UtcNow.AddHours(-2), point.Local);
    }

    [Fact]
    public async Task GetAsync_ClampsLookbackToOneYear()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        db.AuthEvents.Add(new AuthEvent
        {
            Id = Guid.NewGuid(),
            UserId = "old-user",
            Event = AuthenticationEventNames.LoginSucceeded,
            WhenUtc = clock.UtcNow.AddDays(-400)
        });
        await db.SaveChangesAsync();

        var service = new LoginAnalyticsService(db, clock);
        var result = await service.GetAsync(
            10_000,
            markWeekendOdd: false,
            TimeZoneInfo.Utc,
            TimeSpan.FromHours(8),
            TimeSpan.FromHours(18));

        Assert.Empty(result.Points);
    }

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
