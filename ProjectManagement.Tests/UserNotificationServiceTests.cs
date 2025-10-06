using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
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

    [Fact]
    public async Task ProjectAsync_IncludesAccessibleProjectNames()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);

        context.Projects.Add(new Project
        {
            Id = 42,
            Name = "Project Nebula",
            CreatedByUserId = "creator-1",
            LeadPoUserId = "user-1",
        });

        await context.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var notifications = new[]
        {
            new Notification
            {
                Id = 1,
                RecipientUserId = "user-1",
                ProjectId = 42,
                CreatedUtc = clock.UtcNow.UtcDateTime,
            }
        };

        var results = await service.ProjectAsync(principal, "user-1", notifications, default);

        var result = Assert.Single(results);
        Assert.Equal(42, result.ProjectId);
        Assert.Equal("Project Nebula", result.ProjectName);
    }

    [Fact]
    public async Task ListAsync_PrioritizesUnreadAndCountsAccessibleItems()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);

        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var userId = "user-1";
        var now = clock.UtcNow.UtcDateTime;

        context.Projects.AddRange(
            new Project
            {
                Id = 1,
                Name = "Accessible",
                LeadPoUserId = userId,
            },
            new Project
            {
                Id = 2,
                Name = "Restricted",
                LeadPoUserId = "other-user",
            });

        context.Notifications.AddRange(
            new Notification
            {
                Id = 1,
                RecipientUserId = userId,
                ProjectId = 1,
                CreatedUtc = now.AddMinutes(5),
                ReadUtc = now.AddMinutes(6),
            },
            new Notification
            {
                Id = 2,
                RecipientUserId = userId,
                ProjectId = 1,
                CreatedUtc = now.AddMinutes(4),
                ReadUtc = now.AddMinutes(5),
            },
            new Notification
            {
                Id = 3,
                RecipientUserId = userId,
                ProjectId = 2,
                CreatedUtc = now.AddMinutes(3),
            },
            new Notification
            {
                Id = 4,
                RecipientUserId = userId,
                ProjectId = 1,
                CreatedUtc = now.AddMinutes(2),
            },
            new Notification
            {
                Id = 5,
                RecipientUserId = userId,
                ProjectId = 1,
                CreatedUtc = now.AddMinutes(1),
            },
            new Notification
            {
                Id = 6,
                RecipientUserId = userId,
                ProjectId = 1,
                CreatedUtc = now,
                ReadUtc = now.AddMinutes(1),
            });

        await context.SaveChangesAsync();

        var listOptions = new NotificationListOptions
        {
            Limit = 3,
        };

        var results = await service.ListAsync(principal, userId, listOptions, default);
        var unreadCount = await service.CountUnreadAsync(principal, userId, default);

        Assert.Equal(3, results.Count);
        Assert.Collection(results,
            item => Assert.Equal(4, item.Id),
            item => Assert.Equal(5, item.Id),
            item => Assert.Equal(1, item.Id));

        Assert.Equal(2, results.Count(item => item.ReadUtc is null));
        Assert.Equal(unreadCount, results.Count(item => item.ReadUtc is null));
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
