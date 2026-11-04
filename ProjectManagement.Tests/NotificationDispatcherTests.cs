using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Contracts.Notifications;
using ProjectManagement.Data;
using ProjectManagement.Hubs;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NotificationDispatcherTests
{
    [Fact]
    public async Task ProcessBatchAsync_SendsRealtimeNotificationAndUnreadCount()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
            .Options;

        var hub = new RecordingHubContext();
        var clock = new FixedClock(new DateTimeOffset(2024, 10, 1, 12, 0, 0, TimeSpan.Zero));

        await using var provider = CreateProvider(options, clock, hub);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            await userManager.CreateAsync(new ApplicationUser { Id = "recipient-1", UserName = "recipient@example.com" });

            db.Projects.Add(new Project
            {
                Id = 1,
                Name = "Project Orion",
                CreatedByUserId = "creator-1",
                LeadPoUserId = "recipient-1",
            });

            db.NotificationDispatches.Add(new NotificationDispatch
            {
                RecipientUserId = "recipient-1",
                Kind = NotificationKind.RemarkCreated,
                PayloadJson = "{}",
                CreatedUtc = new DateTime(2024, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                Title = "New Remark",
                Summary = "A remark was created.",
                Route = "/remarks/1",
                ProjectId = 1,
            });

            await db.SaveChangesAsync();
        }

        var processed = await RunBatchAsync(provider, clock);

        Assert.True(processed);

        var client = hub.GetClient("recipient-1");
        var notification = Assert.Single(client.Notifications);
        Assert.Equal("New Remark", notification.Title);
        Assert.Equal("/remarks/1", notification.Route);
        Assert.Equal(1, notification.ProjectId);
        Assert.Equal("Project Orion", notification.ProjectName);

        var unreadCount = Assert.Single(client.UnreadCounts);
        Assert.Equal(1, unreadCount);

        using (var verificationScope = provider.CreateScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await db.Notifications.AsNoTracking().SingleAsync();
            var dispatch = await db.NotificationDispatches.AsNoTracking().SingleAsync();

            Assert.Equal(NotificationKind.RemarkCreated, persisted.Kind);
            Assert.Equal(dispatch.Id, persisted.SourceDispatchId);
            Assert.Equal(new DateTime(2024, 10, 1, 12, 0, 0, DateTimeKind.Utc), persisted.CreatedUtc);
            Assert.Equal(new DateTime(2024, 10, 1, 12, 0, 0, DateTimeKind.Utc), persisted.DeliveredUtc);
            Assert.Equal(persisted.DeliveredUtc, dispatch.DispatchedUtc);
        }
    }

    [Fact]
    public async Task ProcessBatchAsync_FailedNotificationInsertLeavesDispatchPendingForRetry()
    {
        // Simulates a transient database failure on the notification insert, e.g. a dropped connection.
        var failingInsert = new FailNotificationInsertInterceptor { RemainingFailures = 1 };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
            .AddInterceptors(failingInsert)
            .Options;
        var hub = new RecordingHubContext();
        var clock = new FixedClock(new DateTimeOffset(2024, 10, 1, 12, 0, 0, TimeSpan.Zero));
        await using var provider = CreateProvider(options, clock, hub);
        await SeedRecipientAndDispatchAsync(provider);

        Assert.True(await RunBatchAsync(provider, clock));

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dispatch = await db.NotificationDispatches.AsNoTracking().SingleAsync();

            // Before the fix the completion stamp was saved with the error, so the dispatch was
            // never claimed again and the notification was silently lost.
            Assert.Null(dispatch.DispatchedUtc);
            Assert.Null(dispatch.DeadLetteredUtc);
            Assert.Equal(1, dispatch.AttemptCount);
            Assert.Contains("Simulated", dispatch.Error);
            Assert.Equal(new DateTime(2024, 10, 1, 12, 0, 5, DateTimeKind.Utc), dispatch.LockedUntilUtc);
            Assert.False(await db.Notifications.AnyAsync());
        }

        // After the retry delay the dispatch is claimed again and delivered exactly once.
        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        Assert.True(await RunBatchAsync(provider, clock));

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dispatch = await db.NotificationDispatches.AsNoTracking().SingleAsync();
            var notification = await db.Notifications.AsNoTracking().SingleAsync();

            Assert.NotNull(dispatch.DispatchedUtc);
            Assert.Null(dispatch.Error);
            Assert.Equal(2, dispatch.AttemptCount);
            Assert.Equal(dispatch.Id, notification.SourceDispatchId);
        }
    }

    [Fact]
    public async Task ProcessBatchAsync_DeadLettersDispatchClaimedTooOftenWithoutCompleting()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
            .Options;
        var hub = new RecordingHubContext();
        var clock = new FixedClock(new DateTimeOffset(2024, 10, 1, 12, 0, 0, TimeSpan.Zero));
        await using var provider = CreateProvider(options, clock, hub);
        // Eight earlier claims ended without completion or a recorded failure (e.g. the process stopped).
        await SeedRecipientAndDispatchAsync(provider, attemptCount: 8);

        Assert.True(await RunBatchAsync(provider, clock));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dispatch = await db.NotificationDispatches.AsNoTracking().SingleAsync();

        Assert.NotNull(dispatch.DeadLetteredUtc);
        Assert.Null(dispatch.DispatchedUtc);
        Assert.Null(dispatch.LockToken);
        Assert.Equal(9, dispatch.AttemptCount);
        Assert.False(await db.Notifications.AnyAsync());
    }

    internal static async Task SeedRecipientAndDispatchAsync(IServiceProvider provider, int attemptCount = 0)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await userManager.CreateAsync(new ApplicationUser { Id = "recipient-1", UserName = "recipient@example.com" });
        db.NotificationDispatches.Add(new NotificationDispatch
        {
            RecipientUserId = "recipient-1",
            Kind = NotificationKind.RemarkCreated,
            PayloadJson = "{}",
            CreatedUtc = new DateTime(2024, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            Title = "New Remark",
            AttemptCount = attemptCount,
        });
        await db.SaveChangesAsync();
    }

    internal static ServiceProvider CreateProvider(
        DbContextOptions<ApplicationDbContext> options,
        IClock clock,
        RecordingHubContext hub)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton<IOptions<IdentityOptions>>(Options.Create(new IdentityOptions()));
        services.AddSingleton<ILogger<UserManager<ApplicationUser>>>(NullLogger<UserManager<ApplicationUser>>.Instance);
        services.AddLogging();
        services.AddScoped(_ => new ApplicationDbContext(options));
        services.AddScoped<IUserStore<ApplicationUser>>(sp => new UserStore<ApplicationUser>(sp.GetRequiredService<ApplicationDbContext>()));
        services.AddScoped(sp => CreateUserManager(sp));
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, TestUserClaimsPrincipalFactory>();
        services.AddScoped<INotificationPreferenceService>(_ => new TestPreferenceService());
        services.AddScoped<INotificationDeliveryService, NotificationDeliveryService>();
        services.AddScoped<UserNotificationService>();
        services.AddSingleton<IHubContext<NotificationsHub, INotificationsClient>>(hub);
        return services.BuildServiceProvider();
    }

    internal static Task<bool> RunBatchAsync(IServiceProvider provider, IClock clock)
    {
        var dispatcher = new NotificationDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<NotificationDispatcher>.Instance);

        var processBatch = typeof(NotificationDispatcher)
            .GetMethod("ProcessBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (Task<bool>)processBatch.Invoke(dispatcher, new object[] { CancellationToken.None })!;
    }

    private static UserManager<ApplicationUser> CreateUserManager(IServiceProvider services)
    {
        return new UserManager<ApplicationUser>(
            services.GetRequiredService<IUserStore<ApplicationUser>>(),
            services.GetRequiredService<IOptions<IdentityOptions>>(),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            services.GetRequiredService<ILogger<UserManager<ApplicationUser>>>());
    }

    private sealed class FailNotificationInsertInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public int RemainingFailures { get; set; }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var insertsNotification = eventData.Context!.ChangeTracker
                .Entries<Notification>()
                .Any(entry => entry.State == EntityState.Added);

            if (insertsNotification && RemainingFailures > 0)
            {
                RemainingFailures--;
                throw new DbUpdateException("Simulated transient failure while saving the notification.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    internal sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class TestUserClaimsPrincipalFactory : IUserClaimsPrincipalFactory<ApplicationUser>
    {
        public Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
        {
            if (user is null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));

            if (!string.IsNullOrEmpty(user.UserName))
            {
                identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
            }

            return Task.FromResult(new ClaimsPrincipal(identity));
        }
    }

    internal sealed class RecordingHubContext : IHubContext<NotificationsHub, INotificationsClient>
    {
        private readonly RecordingHubClients _clients = new();

        public IHubClients<INotificationsClient> Clients => _clients;

        public IGroupManager Groups { get; } = new NoOpGroupManager();

        public RecordingNotificationsClient GetClient(string userId)
        {
            return _clients.GetClient(userId);
        }
    }

    internal sealed class RecordingHubClients : IHubClients<INotificationsClient>
    {
        private readonly ConcurrentDictionary<string, RecordingNotificationsClient> _userClients = new(StringComparer.Ordinal);

        public INotificationsClient All => throw new NotSupportedException();

        public INotificationsClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public INotificationsClient Client(string connectionId) => throw new NotSupportedException();

        public INotificationsClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public INotificationsClient Group(string groupName) => throw new NotSupportedException();

        public INotificationsClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public INotificationsClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public INotificationsClient User(string userId)
        {
            return GetClient(userId);
        }

        public INotificationsClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

        public RecordingNotificationsClient GetClient(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException("User id is required.", nameof(userId));
            }

            return _userClients.GetOrAdd(userId, _ => new RecordingNotificationsClient());
        }
    }

    internal sealed class RecordingNotificationsClient : INotificationsClient
    {
        public List<NotificationListItem> Notifications { get; } = new();

        public List<int> UnreadCounts { get; } = new();

        public List<IReadOnlyList<NotificationListItem>> NotificationLists { get; } = new();

        public Task ReceiveNotification(NotificationListItem notification)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task ReceiveNotifications(IReadOnlyList<NotificationListItem> notifications)
        {
            NotificationLists.Add(notifications);
            return Task.CompletedTask;
        }

        public Task ReceiveUnreadCount(int count)
        {
            UnreadCounts.Add(count);
            return Task.CompletedTask;
        }

        public Task ReceiveNotificationStateChanged(NotificationMutationDto mutation)
            => Task.CompletedTask;

        public Task ReceiveNotificationSeen(NotificationSeenDto mutation)
            => Task.CompletedTask;

        public Task ReceiveProjectMuteChanged(NotificationProjectMuteDto mutation)
            => Task.CompletedTask;
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
