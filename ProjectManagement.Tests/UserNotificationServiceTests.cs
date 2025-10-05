using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class UserNotificationServiceTests
{
    [Fact]
    public async Task ProjectAsync_NormalizesProjectRouteSegments()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);

        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var notifications = new[]
        {
            new Notification
            {
                Id = 1,
                RecipientUserId = "user-1",
                Route = "/projects2/kanbans/54",
                CreatedUtc = clock.UtcNow.UtcDateTime,
            }
        };

        var results = await service.ProjectAsync(principal, "user-1", notifications, default);

        var result = Assert.Single(results);
        Assert.Equal("/projects/2/kanbans/54", result.Route);
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
