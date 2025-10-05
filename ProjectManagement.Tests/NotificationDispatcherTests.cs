using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        var services = new ServiceCollection();
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

        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            await userManager.CreateAsync(new ApplicationUser { Id = "recipient-1", UserName = "recipient@example.com" });

            db.NotificationDispatches.Add(new NotificationDispatch
            {
                RecipientUserId = "recipient-1",
                Kind = NotificationKind.RemarkCreated,
                PayloadJson = "{}",
                CreatedUtc = new DateTime(2024, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                Title = "New Remark",
                Summary = "A remark was created.",
                Route = "/remarks/1",
            });

            await db.SaveChangesAsync();
        }

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var clock = new FixedClock(new DateTimeOffset(2024, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var dispatcher = new NotificationDispatcher(scopeFactory, clock, NullLogger<NotificationDispatcher>.Instance);

        var processBatch = typeof(NotificationDispatcher)
            .GetMethod("ProcessBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var resultTask = (Task<bool>)processBatch.Invoke(dispatcher, new object[] { CancellationToken.None })!;
        var processed = await resultTask;

        Assert.True(processed);

        var client = hub.GetClient("recipient-1");
        var notification = Assert.Single(client.Notifications);
        Assert.Equal("New Remark", notification.Title);
        Assert.Equal("/remarks/1", notification.Route);

        var unreadCount = Assert.Single(client.UnreadCounts);
        Assert.Equal(1, unreadCount);
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

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; }
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

    private sealed class RecordingHubContext : IHubContext<NotificationsHub, INotificationsClient>
    {
        private readonly RecordingHubClients _clients = new();

        public IHubClients<INotificationsClient> Clients => _clients;

        public IGroupManager Groups { get; } = new NoOpGroupManager();

        public RecordingNotificationsClient GetClient(string userId)
        {
            return _clients.GetClient(userId);
        }
    }

    private sealed class RecordingHubClients : IHubClients<INotificationsClient>
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

    private sealed class RecordingNotificationsClient : INotificationsClient
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
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
