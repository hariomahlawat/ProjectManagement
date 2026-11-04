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
                Assert.Equal("Noting sheets – 1 · 127133026/2/2026", document.Summary);
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


    [Fact]
    public async Task ListPageAsync_FiltersGmailFoldersAndReturnsFolderCounts()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);
        var principal = CreatePrincipal("user-1");
        var now = clock.UtcNow.UtcDateTime;

        context.Projects.AddRange(
            new Project { Id = 1, Name = "Active Project", LeadPoUserId = "user-1" },
            new Project { Id = 2, Name = "Muted Project", LeadPoUserId = "user-1" });
        context.UserProjectMutes.Add(new UserProjectMute { UserId = "user-1", ProjectId = 2 });
        context.Notifications.AddRange(
            new Notification
            {
                Id = 1,
                RecipientUserId = "user-1",
                ProjectId = 1,
                Kind = NotificationKind.StageStatusChanged,
                CreatedUtc = now,
            },
            new Notification
            {
                Id = 2,
                RecipientUserId = "user-1",
                ProjectId = 1,
                Kind = NotificationKind.DocumentPublished,
                CreatedUtc = now.AddMinutes(-1),
                ReadUtc = now,
            },
            new Notification
            {
                Id = 3,
                RecipientUserId = "user-1",
                ProjectId = 1,
                Kind = NotificationKind.PlanSubmitted,
                CreatedUtc = now.AddMinutes(-2),
            },
            new Notification
            {
                Id = 4,
                RecipientUserId = "user-1",
                ProjectId = 2,
                Kind = NotificationKind.StageStatusChanged,
                CreatedUtc = now.AddMinutes(-3),
            });
        await context.SaveChangesAsync();

        var inbox = await service.ListPageAsync(
            principal,
            "user-1",
            new NotificationListOptions { Folder = "inbox" });
        var approvals = await service.ListPageAsync(
            principal,
            "user-1",
            new NotificationListOptions { Folder = "approvals" });
        var muted = await service.ListPageAsync(
            principal,
            "user-1",
            new NotificationListOptions { Folder = "muted" });

        Assert.Equal(new[] { 1, 2, 3 }, inbox.Items.Select(item => item.Id));
        Assert.Single(approvals.Items);
        Assert.Equal(3, approvals.Items[0].Id);
        Assert.Single(muted.Items);
        Assert.Equal(4, muted.Items[0].Id);

        var inboxCount = Assert.Single(inbox.Folders.Where(folder => folder.Key == "inbox"));
        var unreadCount = Assert.Single(inbox.Folders.Where(folder => folder.Key == "unread"));
        var documentCount = Assert.Single(inbox.Folders.Where(folder => folder.Key == "documents"));
        var mutedCount = Assert.Single(inbox.Folders.Where(folder => folder.Key == "muted"));

        Assert.Equal(3, inboxCount.TotalCount);
        Assert.Equal(2, inboxCount.UnreadCount);
        Assert.Equal(2, unreadCount.TotalCount);
        Assert.Equal(1, documentCount.TotalCount);
        Assert.Equal(1, mutedCount.TotalCount);
    }

    [Theory]
    [InlineData(NotificationKind.PlanWithdrawn, false)]
    [InlineData(NotificationKind.PlanWithdrawn, true)]
    [InlineData(NotificationKind.PlanSuperseded, false)]
    [InlineData(NotificationKind.PlanSuperseded, true)]
    public async Task ListPageAsync_TerminalPlanNoticesStayInApprovalsButNotActionRequired(
        NotificationKind terminalKind, bool isRead)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);
        var principal = CreatePrincipal("user-1");
        var now = clock.UtcNow.UtcDateTime;

        context.Projects.Add(new Project { Id = 1, Name = "Project", LeadPoUserId = "user-1" });
        context.Notifications.AddRange(
            new Notification
            {
                Id = 1, RecipientUserId = "user-1", ProjectId = 1,
                Kind = terminalKind, CreatedUtc = now,
                ReadUtc = isRead ? now : null,
            },
            new Notification
            {
                Id = 2, RecipientUserId = "user-1", ProjectId = 1,
                Kind = NotificationKind.PlanSubmitted, CreatedUtc = now.AddMinutes(-1),
            },
            new Notification
            {
                Id = 3, RecipientUserId = "user-1", ProjectId = 1,
                Kind = NotificationKind.PlanRejected, CreatedUtc = now.AddMinutes(-2), ReadUtc = now,
            });
        await context.SaveChangesAsync();

        var action = await service.ListPageAsync(principal, "user-1",
            new NotificationListOptions { Folder = "action", IncludeFilterOptions = true });
        var approvals = await service.ListPageAsync(principal, "user-1",
            new NotificationListOptions { Folder = "approvals", IncludeFilterOptions = true });

        Assert.Equal(new[] { 2, 3 }, action.Items.Select(item => item.Id));
        Assert.Equal(2, action.TotalCount);
        var actionCount = Assert.Single(action.Folders.Where(folder => folder.Key == "action"));
        Assert.Equal(2, actionCount.TotalCount);
        Assert.Equal(1, actionCount.UnreadCount);

        Assert.Equal(new[] { 1, 2, 3 }, approvals.Items.Select(item => item.Id));
        Assert.Equal(3, approvals.TotalCount);
        var approvalCount = Assert.Single(approvals.Folders.Where(folder => folder.Key == "approvals"));
        Assert.Equal(3, approvalCount.TotalCount);
        Assert.Equal(isRead ? 1 : 2, approvalCount.UnreadCount);
    }

    [Fact]
    public async Task CollaborationFolder_IncludesNotebookCollaborationNotifications()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2026, 7, 2, 4, 0, 0, TimeSpan.Zero));
        var service = new UserNotificationService(context, clock);
        var principal = CreatePrincipal("user-1");
        var now = clock.UtcNow.UtcDateTime;

        context.Notifications.AddRange(
            new Notification
            {
                Id = 1,
                RecipientUserId = "user-1",
                Kind = NotificationKind.NotebookShared,
                Module = "Notebook",
                CreatedUtc = now,
            },
            new Notification
            {
                Id = 2,
                RecipientUserId = "user-1",
                Kind = NotificationKind.NotebookAccessRemoved,
                Module = "Notebook",
                CreatedUtc = now.AddMinutes(-1),
            },
            new Notification
            {
                Id = 3,
                RecipientUserId = "user-1",
                Kind = NotificationKind.NotebookCollaborationLeft,
                Module = "Notebook",
                CreatedUtc = now.AddMinutes(-2),
                ReadUtc = now,
            });
        await context.SaveChangesAsync();

        var collaboration = await service.ListPageAsync(
            principal,
            "user-1",
            new NotificationListOptions
            {
                Folder = "collaboration",
                IncludeFilterOptions = true,
            });

        Assert.Equal(new[] { 1, 2, 3 }, collaboration.Items.Select(item => item.Id));
        var folder = Assert.Single(collaboration.Folders.Where(item => item.Key == "collaboration"));
        Assert.Equal(3, folder.TotalCount);
        Assert.Equal(2, folder.UnreadCount);
    }

    [Fact]
    public async Task ProjectAsync_InfersLegacyStageAndDocumentKindsWhenKindIsMissing()
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

        var results = await service.ProjectAsync(
            CreatePrincipal("user-1"),
            "user-1",
            new[]
            {
                new Notification
                {
                    Id = 1,
                    RecipientUserId = "user-1",
                    ProjectId = 42,
                    Module = "Stages",
                    EventType = "StageStatusChanged",
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
                    Module = "Documents",
                    EventType = "DocumentPublished",
                    Title = "Project Nebula document noting sheets - 1-3676667439909_127133026_2_2026 published",
                    Summary = "Document noting sheets - 1-3676667439909_127133026_2_2026 was published.",
                    CreatedUtc = clock.UtcNow.UtcDateTime.AddMinutes(-1),
                },
            });

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
                Assert.Equal("Noting sheets – 1 · 127133026/2/2026", document.Summary);
            });
    }

    [Fact]
    public async Task CountUnseenAsync_CountsOnlyUnseenUnmutedAccessibleItems()
    {
        var (context, service, clock) = CreateService();
        await using var _ = context;
        const string userId = "user-1";
        var now = clock.UtcNow.UtcDateTime;

        context.Projects.AddRange(
            new Project { Id = 1, Name = "Open", LeadPoUserId = userId },
            new Project { Id = 2, Name = "Muted", LeadPoUserId = userId });
        context.UserProjectMutes.Add(new UserProjectMute { UserId = userId, ProjectId = 2 });
        context.Notifications.AddRange(
            // Unseen and unread: counted.
            new Notification { Id = 1, RecipientUserId = userId, ProjectId = 1, CreatedUtc = now },
            // Seen but still unread: no longer counted by the badge, still counted as unread.
            new Notification { Id = 2, RecipientUserId = userId, ProjectId = 1, CreatedUtc = now, SeenUtc = now },
            // No project: counted.
            new Notification { Id = 3, RecipientUserId = userId, CreatedUtc = now },
            // Muted project: excluded.
            new Notification { Id = 4, RecipientUserId = userId, ProjectId = 2, CreatedUtc = now },
            // Another recipient: excluded.
            new Notification { Id = 5, RecipientUserId = "user-2", CreatedUtc = now },
            // Legacy row read without a seen stamp: read implies seen, so not counted.
            new Notification { Id = 6, RecipientUserId = userId, CreatedUtc = now, ReadUtc = now });
        await context.SaveChangesAsync();

        var principal = CreatePrincipal(userId);
        var counts = await service.CountAsync(principal, userId);

        Assert.Equal(2, await service.CountUnseenAsync(principal, userId));
        Assert.Equal(3, counts.UnreadCount);
        Assert.Equal(2, counts.UnseenCount);
        Assert.Equal(6, counts.LatestNotificationId);
        Assert.False(string.IsNullOrEmpty(counts.ListVersion));
    }

    [Fact]
    public async Task MarkAllSeenAsync_ClearsBadgeWithoutChangingReadStateAndHonoursUpToId()
    {
        var (context, service, clock) = CreateService();
        await using var _ = context;
        const string userId = "user-1";
        var now = clock.UtcNow.UtcDateTime;

        context.Notifications.AddRange(
            new Notification { Id = 1, RecipientUserId = userId, CreatedUtc = now.AddMinutes(-3) },
            new Notification { Id = 2, RecipientUserId = userId, CreatedUtc = now.AddMinutes(-2) },
            // Arrived after the bell list was loaded (id above upToId): stays unseen.
            new Notification { Id = 3, RecipientUserId = userId, CreatedUtc = now.AddMinutes(-1) },
            new Notification { Id = 4, RecipientUserId = "user-2", CreatedUtc = now });
        await context.SaveChangesAsync();

        var principal = CreatePrincipal(userId);
        var result = await service.MarkAllSeenAsync(principal, userId, upToId: 2);

        Assert.Equal(NotificationOperationResult.Success, result.Result);
        Assert.True(result.AppliesToAll);
        Assert.Equal(2, result.AffectedCount);

        var rows = await context.Notifications.AsNoTracking().OrderBy(n => n.Id).ToListAsync();
        Assert.NotNull(rows[0].SeenUtc);
        Assert.NotNull(rows[1].SeenUtc);
        Assert.Null(rows[2].SeenUtc);
        Assert.Null(rows[3].SeenUtc);
        Assert.All(rows, row => Assert.Null(row.ReadUtc));

        Assert.Equal(1, await service.CountUnseenAsync(principal, userId));
        Assert.Equal(3, await service.CountUnreadAsync(principal, userId));

        var all = await service.MarkAllSeenAsync(principal, userId, upToId: null);
        Assert.Equal(1, all.AffectedCount);
        Assert.Equal(0, await service.CountUnseenAsync(principal, userId));
    }

    [Fact]
    public async Task ListPageAsync_ActionRequiredFirstListsUnreadActionItemsBeforeNewerUpdates()
    {
        var (context, service, clock) = CreateService();
        await using var _ = context;
        const string userId = "user-1";
        var now = clock.UtcNow.UtcDateTime;

        context.Notifications.AddRange(
            new Notification { Id = 1, RecipientUserId = userId, Kind = NotificationKind.PlanSubmitted, CreatedUtc = now.AddDays(-3) },
            new Notification { Id = 2, RecipientUserId = userId, Kind = NotificationKind.DocumentPublished, CreatedUtc = now.AddMinutes(-1) },
            new Notification { Id = 3, RecipientUserId = userId, Kind = NotificationKind.RemarkCreated, CreatedUtc = now.AddMinutes(-2) },
            // Action-required but already read: ordered by time like any other row.
            new Notification { Id = 4, RecipientUserId = userId, Kind = NotificationKind.ActionTaskAssigned, CreatedUtc = now.AddDays(-1), ReadUtc = now },
            new Notification { Id = 5, RecipientUserId = userId, Kind = NotificationKind.MentionedInRemark, CreatedUtc = now.AddDays(-2) });
        await context.SaveChangesAsync();

        var principal = CreatePrincipal(userId);
        var page = await service.ListPageAsync(
            principal,
            userId,
            new NotificationListOptions { Limit = 4, ActionRequiredFirst = true, IncludeFilterOptions = false });

        Assert.Equal(new[] { 5, 1, 2, 3 }, page.Items.Select(item => item.Id).ToArray());
        Assert.True(page.HasMore);
        Assert.Null(page.NextCursor);

        var chronological = await service.ListPageAsync(
            principal,
            userId,
            new NotificationListOptions { Limit = 4, IncludeFilterOptions = false });

        Assert.Equal(new[] { 2, 3, 4, 5 }, chronological.Items.Select(item => item.Id).ToArray());
        Assert.NotNull(chronological.NextCursor);

        // The seen-all boundary is the newest row in the snapshot, not the newest row shown.
        var actionOnly = await service.ListPageAsync(
            principal,
            userId,
            new NotificationListOptions { Limit = 2, ActionRequiredFirst = true, IncludeFilterOptions = false });
        Assert.Equal(new[] { 5, 1 }, actionOnly.Items.Select(item => item.Id).ToArray());
        Assert.Equal(5, actionOnly.LatestNotificationId);

        context.Notifications.Add(new Notification { Id = 9, RecipientUserId = userId, Kind = NotificationKind.DocumentPublished, CreatedUtc = now });
        await context.SaveChangesAsync();
        actionOnly = await service.ListPageAsync(
            principal,
            userId,
            new NotificationListOptions { Limit = 2, ActionRequiredFirst = true, IncludeFilterOptions = false });
        Assert.DoesNotContain(9, actionOnly.Items.Select(item => item.Id));
        Assert.Equal(9, actionOnly.LatestNotificationId);
    }

    [Fact]
    public async Task ListPageAsync_SetsGroupKeysForRoutineRowsOnly()
    {
        var (context, service, clock) = CreateService();
        await using var _ = context;
        const string userId = "user-1";
        var now = clock.UtcNow.UtcDateTime;

        context.Projects.Add(new Project { Id = 7, Name = "Orion", LeadPoUserId = userId });
        context.Notifications.AddRange(
            new Notification { Id = 1, RecipientUserId = userId, ProjectId = 7, Kind = NotificationKind.DocumentPublished, CreatedUtc = now },
            new Notification { Id = 2, RecipientUserId = userId, ProjectId = 7, Kind = NotificationKind.DocumentReplaced, CreatedUtc = now.AddMinutes(-5) },
            new Notification { Id = 3, RecipientUserId = userId, ProjectId = 7, Kind = NotificationKind.PlanSubmitted, CreatedUtc = now.AddMinutes(-10) });
        await context.SaveChangesAsync();

        var page = await service.ListPageAsync(
            CreatePrincipal(userId),
            userId,
            new NotificationListOptions { IncludeFilterOptions = false });

        var byId = page.Items.ToDictionary(item => item.Id);
        Assert.NotNull(byId[1].GroupKey);
        Assert.Equal(byId[1].GroupKey, byId[2].GroupKey);
        Assert.Null(byId[3].GroupKey);
    }

    private static (ApplicationDbContext Context, UserNotificationService Service, TestClock Clock) CreateService()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-notification-tests-{Guid.NewGuid()}")
            .Options;
        var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 12, 0, 0, TimeSpan.Zero));
        return (context, new UserNotificationService(context, clock), clock);
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
