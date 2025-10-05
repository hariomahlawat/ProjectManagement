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
}
