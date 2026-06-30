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

        var results = await service.ProjectAsync(CreatePrincipal("user-1"), "user-1", notifications, default);

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

        var results = await service.ProjectAsync(CreatePrincipal("user-1"), "user-1", notifications, default);

        var result = Assert.Single(results);
        Assert.Equal(42, result.ProjectId);
        Assert.Equal("Project Nebula", result.ProjectName);
    }

    [Fact]
    public async Task ListAsync_OrdersNewestFirstAndCountsAccessibleUnreadItems()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);

        var userId = "user-1";
        var principal = CreatePrincipal(userId);
        var now = clock.UtcNow.UtcDateTime;

        context.Projects.Add(new Project { Id = 1, Name = "Accessible", LeadPoUserId = userId });
        context.Notifications.AddRange(
            new Notification { Id = 1, RecipientUserId = userId, ProjectId = 1, CreatedUtc = now.AddMinutes(5), ReadUtc = now.AddMinutes(6) },
            new Notification { Id = 2, RecipientUserId = userId, ProjectId = 1, CreatedUtc = now.AddMinutes(4), ReadUtc = now.AddMinutes(5) },
            new Notification { Id = 3, RecipientUserId = userId, ProjectId = 999, CreatedUtc = now.AddMinutes(3) },
            new Notification { Id = 4, RecipientUserId = userId, ProjectId = 1, CreatedUtc = now.AddMinutes(2) },
            new Notification { Id = 5, RecipientUserId = userId, ProjectId = 1, CreatedUtc = now.AddMinutes(1) },
            new Notification { Id = 6, RecipientUserId = userId, ProjectId = 1, CreatedUtc = now, ReadUtc = now.AddMinutes(1) });
        await context.SaveChangesAsync();

        var results = await service.ListAsync(principal, userId, new NotificationListOptions { Limit = 3 }, default);
        var unreadCount = await service.CountUnreadAsync(principal, userId, default);

        Assert.Collection(results,
            item => Assert.Equal(1, item.Id),
            item => Assert.Equal(2, item.Id),
            item => Assert.Equal(4, item.Id));
        Assert.Equal(2, unreadCount);
    }


    [Fact]
    public async Task ListPageAsync_UsesStableCursorAndServerSideSearch()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);
        var principal = CreatePrincipal("user-1");
        var now = clock.UtcNow.UtcDateTime;

        context.Projects.Add(new Project { Id = 1, Name = "Project Atlas", LeadPoUserId = "user-1" });
        context.Notifications.AddRange(
            new Notification { Id = 1, RecipientUserId = "user-1", ProjectId = 1, Title = "Alpha", CreatedUtc = now },
            new Notification { Id = 2, RecipientUserId = "user-1", ProjectId = 1, Title = "Bravo", CreatedUtc = now.AddMinutes(-1) },
            new Notification { Id = 3, RecipientUserId = "user-1", ProjectId = 1, Title = "Charlie", CreatedUtc = now.AddMinutes(-2) },
            new Notification { Id = 4, RecipientUserId = "user-1", ProjectId = 1, Title = "Delta", CreatedUtc = now.AddMinutes(-3) });
        await context.SaveChangesAsync();

        var first = await service.ListPageAsync(
            principal,
            "user-1",
            new NotificationListOptions { Limit = 2 });
        var second = await service.ListPageAsync(
            principal,
            "user-1",
            new NotificationListOptions { Limit = 2, Cursor = first.NextCursor });
        var search = await service.ListPageAsync(
            principal,
            "user-1",
            new NotificationListOptions { Search = "Atlas" });

        Assert.Equal(new[] { 1, 2 }, first.Items.Select(item => item.Id));
        Assert.True(first.HasMore);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(new[] { 3, 4 }, second.Items.Select(item => item.Id));
        Assert.False(second.HasMore);
        Assert.Equal(4, search.TotalCount);
    }

    [Fact]
    public async Task MarkAllReadAsync_UpdatesEveryAccessibleUnreadNotificationWithBoundedMutationPayload()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);
        var principal = CreatePrincipal("user-1");

        context.Projects.Add(new Project { Id = 1, Name = "Project", LeadPoUserId = "user-1" });
        context.Notifications.AddRange(
            new Notification { RecipientUserId = "user-1", ProjectId = 1, CreatedUtc = clock.UtcNow.UtcDateTime },
            new Notification { RecipientUserId = "user-1", ProjectId = 1, CreatedUtc = clock.UtcNow.UtcDateTime.AddMinutes(-1) },
            new Notification { RecipientUserId = "other", ProjectId = 1, CreatedUtc = clock.UtcNow.UtcDateTime });
        await context.SaveChangesAsync();

        var result = await service.MarkAllReadAsync(principal, "user-1");

        Assert.Equal(NotificationOperationResult.Success, result.Result);
        Assert.True(result.AppliesToAll);
        Assert.Equal(2, result.AffectedCount);
        Assert.Empty(result.NotificationIds);
        Assert.All(
            await context.Notifications.Where(n => n.RecipientUserId == "user-1").ToListAsync(),
            notification =>
            {
                Assert.NotNull(notification.ReadUtc);
                Assert.NotNull(notification.SeenUtc);
            });
    }

    [Fact]
    public async Task MutingProject_MarksExistingRowsReadAndExcludesProjectFromUnreadCount()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);
        var principal = CreatePrincipal("user-1");

        context.Projects.Add(new Project { Id = 1, Name = "Project", LeadPoUserId = "user-1" });
        context.Notifications.Add(new Notification
        {
            Id = 10,
            RecipientUserId = "user-1",
            ProjectId = 1,
            CreatedUtc = clock.UtcNow.UtcDateTime,
        });
        await context.SaveChangesAsync();

        var result = await service.SetProjectMuteDetailedAsync(principal, "user-1", 1, muted: true);
        var count = await service.CountUnreadAsync(principal, "user-1");

        Assert.Equal(NotificationOperationResult.Success, result.Result);
        Assert.True(result.IsMuted);
        Assert.Contains(10, result.ChangedNotificationIds);
        Assert.Equal(0, count);
        Assert.NotNull((await context.Notifications.SingleAsync()).ReadUtc);
    }

    [Fact]
    public async Task ProjectAsync_RewritesLegacyStageAndDocumentTextForDisplay()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);

        context.Projects.Add(new Project
        {
            Id = 42,
            Name = "Project Nebula",
            LeadPoUserId = "user-1",
        });
        await context.SaveChangesAsync();

        var notifications = new[]
        {
            new Notification
            {
                Id = 1,
                RecipientUserId = "user-1",
                ProjectId = 42,
                Kind = NotificationKind.StageStatusChanged,
                ScopeId = "42:TEC",
                Title = "Project Nebula stage TEC Completed",
                Summary = "Stage TEC moved from NotStarted to Completed.",
                CreatedUtc = clock.UtcNow.UtcDateTime,
            },
            new Notification
            {
                Id = 2,
                RecipientUserId = "user-1",
                ProjectId = 42,
                Kind = NotificationKind.DocumentPublished,
                Title = "Project Nebula document noting sheets - 1-3676667439909_127133026_2_2026 published",
                Summary = "Document noting sheets - 1-3676667439909_127133026_2_2026 was published.",
                CreatedUtc = clock.UtcNow.UtcDateTime.AddMinutes(-1),
            },
        };

        var results = await service.ProjectAsync(CreatePrincipal("user-1"), "user-1", notifications, default);

        Assert.Collection(
            results,
            stage =>
            {
                Assert.Equal("TEC stage completed", stage.Title);
                Assert.Equal("Status changed from Not started to Completed.", stage.Summary);
            },
            document =>
            {
                Assert.Equal("Document published", document.Title);
                Assert.Equal("noting sheets – 1", document.Summary);
                Assert.NotNull(document.SummaryTooltip);
            });
    }

    [Fact]
    public async Task ListPageAsync_ExcludesRoutineNotificationsGeneratedByTheRecipient()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);
        var principal = CreatePrincipal("user-1");

        context.Projects.Add(new Project { Id = 1, Name = "Project", LeadPoUserId = "user-1" });
        context.Notifications.AddRange(
            new Notification
            {
                RecipientUserId = "user-1",
                ActorUserId = "user-1",
                ProjectId = 1,
                Kind = NotificationKind.StageStatusChanged,
                CreatedUtc = clock.UtcNow.UtcDateTime,
            },
            new Notification
            {
                RecipientUserId = "user-1",
                ActorUserId = "other-user",
                ProjectId = 1,
                Kind = NotificationKind.StageStatusChanged,
                CreatedUtc = clock.UtcNow.UtcDateTime.AddMinutes(-1),
            });
        await context.SaveChangesAsync();

        var page = await service.ListPageAsync(principal, "user-1", new NotificationListOptions());

        Assert.Single(page.Items);
        Assert.Equal("other-user", page.Items[0].ActorUserId);
        Assert.Equal(1, page.UnreadCount);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
            authenticationType: "Tests");
        return new ClaimsPrincipal(identity);
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
