using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Notifications;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NotificationRetentionServiceTests
{
    [Fact]
    public async Task RunOnceAsync_RemovesNotificationsBeyondAgeAndCountLimits()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"notification-retention-{Guid.NewGuid()}")
            .Options;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));

        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.Notifications.AddRange(
                new Notification
                {
                    RecipientUserId = "user-1",
                    Title = "Old 1",
                    CreatedUtc = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                },
                new Notification
                {
                    RecipientUserId = "user-1",
                    Title = "Old 2",
                    CreatedUtc = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc)
                },
                new Notification
                {
                    RecipientUserId = "user-1",
                    Title = "Keep 1",
                    CreatedUtc = new DateTime(2024, 1, 5, 12, 0, 0, DateTimeKind.Utc)
                },
                new Notification
                {
                    RecipientUserId = "user-1",
                    Title = "Keep 2",
                    CreatedUtc = new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc)
                },
                new Notification
                {
                    RecipientUserId = "user-1",
                    Title = "Keep 3",
                    CreatedUtc = new DateTime(2024, 1, 7, 12, 0, 0, DateTimeKind.Utc)
                },
                new Notification
                {
                    RecipientUserId = "user-1",
                    Title = "Keep 4",
                    CreatedUtc = new DateTime(2024, 1, 8, 12, 0, 0, DateTimeKind.Utc)
                },
                new Notification
                {
                    RecipientUserId = "user-1",
                    Title = "Keep 5",
                    CreatedUtc = new DateTime(2024, 1, 9, 12, 0, 0, DateTimeKind.Utc)
                },
                new Notification
                {
                    RecipientUserId = "user-2",
                    Title = "Other 1",
                    CreatedUtc = new DateTime(2024, 1, 8, 12, 0, 0, DateTimeKind.Utc)
                },
                new Notification
                {
                    RecipientUserId = "user-2",
                    Title = "Other 2",
                    CreatedUtc = new DateTime(2024, 1, 9, 12, 0, 0, DateTimeKind.Utc)
                });

            await db.SaveChangesAsync();
        }

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 1, 10, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new NotificationRetentionOptions
        {
            SweepInterval = TimeSpan.FromMinutes(5),
            MaxAge = TimeSpan.FromDays(7),
            MaxPerUser = 3
        });

        var service = new NotificationRetentionService(
            scopeFactory,
            options,
            clock,
            NullLogger<NotificationRetentionService>.Instance);

        var removed = await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(4, removed);

        using var verificationScope = provider.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user1Notifications = await verificationDb.Notifications
            .Where(n => n.RecipientUserId == "user-1")
            .OrderBy(n => n.CreatedUtc)
            .ToListAsync();

        Assert.Equal(3, user1Notifications.Count);
        Assert.Equal(new[]
        {
            new DateTime(2024, 1, 7, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 8, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 9, 12, 0, 0, DateTimeKind.Utc)
        }, user1Notifications.Select(n => n.CreatedUtc).ToArray());

        var user2Notifications = await verificationDb.Notifications
            .Where(n => n.RecipientUserId == "user-2")
            .OrderBy(n => n.CreatedUtc)
            .ToListAsync();

        Assert.Equal(2, user2Notifications.Count);
    }

    private static readonly DateTime Now = new(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RunSweepAsync_AutoMarksStaleInformationalAfterSevenDaysAndActionRequiredAfterThirty()
    {
        await using var provider = CreateProvider();
        await SeedAsync(provider,
            new Notification { Id = 1, RecipientUserId = "user-1", Kind = NotificationKind.DocumentPublished, CreatedUtc = Now.AddDays(-8) },
            new Notification { Id = 2, RecipientUserId = "user-1", Kind = NotificationKind.DocumentPublished, CreatedUtc = Now.AddDays(-6) },
            // Legacy row without a kind is informational.
            new Notification { Id = 3, RecipientUserId = "user-1", Kind = null, CreatedUtc = Now.AddDays(-8) },
            new Notification { Id = 4, RecipientUserId = "user-1", Kind = NotificationKind.PlanSubmitted, CreatedUtc = Now.AddDays(-10) },
            new Notification { Id = 5, RecipientUserId = "user-1", Kind = NotificationKind.PlanSubmitted, CreatedUtc = Now.AddDays(-31) },
            // Already seen earlier: auto-read keeps the original seen time.
            new Notification { Id = 6, RecipientUserId = "user-1", Kind = NotificationKind.RemarkCreated, CreatedUtc = Now.AddDays(-9), SeenUtc = Now.AddDays(-9) });

        var service = CreateService(provider, new NotificationRetentionOptions
        {
            MaxAge = null,
            MaxPerUser = null,
            CompletedDispatchMaxAge = null,
            DeadLetterMaxAge = null,
        });

        var result = await service.RunSweepAsync(CancellationToken.None);

        Assert.Equal(4, result.AutoReadCount);
        Assert.Equal(0, result.RemovedCount);

        var rows = await LoadAsync(provider);
        Assert.Equal(Now, rows[1].ReadUtc);
        Assert.Equal(Now, rows[1].SeenUtc);
        Assert.Null(rows[2].ReadUtc);
        Assert.Equal(Now, rows[3].ReadUtc);
        Assert.Null(rows[4].ReadUtc);
        Assert.Equal(Now, rows[5].ReadUtc);
        Assert.Equal(Now, rows[6].ReadUtc);
        Assert.Equal(Now.AddDays(-9), rows[6].SeenUtc);
    }

    [Fact]
    public async Task RunSweepAsync_KeepsUnreadActionRequiredRowsBeyondAgeAndPerUserLimits()
    {
        await using var provider = CreateProvider();
        await SeedAsync(provider,
            // Older than MaxAge, unread and action-required: kept.
            new Notification { Id = 1, RecipientUserId = "user-1", Kind = NotificationKind.PlanSubmitted, CreatedUtc = Now.AddDays(-20) },
            // Older than MaxAge but already read: removed.
            new Notification { Id = 2, RecipientUserId = "user-1", Kind = NotificationKind.PlanSubmitted, CreatedUtc = Now.AddDays(-20), ReadUtc = Now.AddDays(-19) },
            // Older than MaxAge and informational: removed.
            new Notification { Id = 3, RecipientUserId = "user-1", Kind = NotificationKind.DocumentPublished, CreatedUtc = Now.AddDays(-20) },
            // Within MaxAge, but beyond MaxPerUser = 2 once newer rows are counted.
            new Notification { Id = 4, RecipientUserId = "user-1", Kind = NotificationKind.ActivityDeleteRequested, CreatedUtc = Now.AddDays(-3) },
            new Notification { Id = 5, RecipientUserId = "user-1", Kind = NotificationKind.StageStatusChanged, CreatedUtc = Now.AddDays(-3).AddMinutes(-1), ReadUtc = Now },
            new Notification { Id = 6, RecipientUserId = "user-1", Kind = NotificationKind.StageStatusChanged, CreatedUtc = Now.AddDays(-1) },
            new Notification { Id = 7, RecipientUserId = "user-1", Kind = NotificationKind.StageStatusChanged, CreatedUtc = Now.AddHours(-1) });

        var service = CreateService(provider, new NotificationRetentionOptions
        {
            MaxAge = TimeSpan.FromDays(14),
            MaxPerUser = 2,
            CompletedDispatchMaxAge = null,
            DeadLetterMaxAge = null,
        });

        var result = await service.RunSweepAsync(CancellationToken.None);

        Assert.Equal(3, result.RemovedCount);
        Assert.Equal(new[] { 1, 4, 6, 7 }, (await LoadAsync(provider)).Keys.OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task RunSweepAsync_DisabledActionAutoReadRemovesProtection()
    {
        await using var provider = CreateProvider();
        await SeedAsync(provider,
            new Notification { Id = 1, RecipientUserId = "user-1", Kind = NotificationKind.PlanSubmitted, CreatedUtc = Now.AddDays(-20) });

        var service = CreateService(provider, new NotificationRetentionOptions
        {
            MaxAge = TimeSpan.FromDays(14),
            AutoReadInformationalAfter = null,
            AutoReadActionRequiredAfter = null,
            CompletedDispatchMaxAge = null,
            DeadLetterMaxAge = null,
        });

        var result = await service.RunSweepAsync(CancellationToken.None);

        Assert.Equal(new NotificationRetentionSweepResult(1, 0), result);
        Assert.Empty(await LoadAsync(provider));
    }

    private static ServiceProvider CreateProvider()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"notification-retention-{Guid.NewGuid()}")
            .Options;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(IServiceProvider provider, params Notification[] notifications)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync();
    }

    private static async Task<System.Collections.Generic.Dictionary<int, Notification>> LoadAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Notifications.AsNoTracking().ToDictionaryAsync(n => n.Id);
    }

    private static NotificationRetentionService CreateService(IServiceProvider provider, NotificationRetentionOptions options)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            FakeClock.AtUtc(new DateTimeOffset(Now)),
            NullLogger<NotificationRetentionService>.Instance);
}
