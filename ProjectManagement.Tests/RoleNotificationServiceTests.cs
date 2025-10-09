using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class RoleNotificationServiceTests
{
    [Fact]
    public async Task NotifyRolesUpdatedAsync_SendsToUser()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService();
        var clock = new IncrementingTestClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new RoleNotificationService(publisher, preferences, clock, NullLogger<RoleNotificationService>.Instance);

        var user = new ApplicationUser { Id = "user-1", UserName = "user.one", FullName = "User One" };

        await service.NotifyRolesUpdatedAsync(user, new[] { "Admin", "HoD" }, new[] { "Reader" }, "actor-1");

        Assert.Single(publisher.Events);
        var evt = publisher.Events[0];
        Assert.Equal(NotificationKind.RoleAssignmentsChanged, evt.Kind);
        Assert.Single(evt.Recipients);
        Assert.Equal("user-1", evt.Recipients.First());
        Assert.Equal("Users", evt.Module);
        Assert.Equal("RoleAssignmentsChanged", evt.EventType);
        Assert.Equal("User", evt.ScopeType);
        Assert.Equal("user-1", evt.ScopeId);
        Assert.Equal("/Identity/Account/Manage", evt.Route);
        Assert.Contains((NotificationKind.RoleAssignmentsChanged, "user-1", null), preferences.Calls);
    }

    [Fact]
    public async Task NotifyRolesUpdatedAsync_RespectsOptOut()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService((kind, userId, _) => false);
        var clock = new IncrementingTestClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new RoleNotificationService(publisher, preferences, clock, NullLogger<RoleNotificationService>.Instance);

        var user = new ApplicationUser { Id = "user-2", UserName = "user.two" };

        await service.NotifyRolesUpdatedAsync(user, new[] { "Admin" }, new string[0], "actor-2");

        Assert.Empty(publisher.Events);
        Assert.Contains((NotificationKind.RoleAssignmentsChanged, "user-2", null), preferences.Calls);
    }

    [Fact]
    public async Task NotifyRolesUpdatedAsync_ProducesUniqueFingerprints()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService();
        var clock = new IncrementingTestClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new RoleNotificationService(publisher, preferences, clock, NullLogger<RoleNotificationService>.Instance);

        var user = new ApplicationUser { Id = "user-3", UserName = "user.three" };

        await service.NotifyRolesUpdatedAsync(user, new[] { "Admin" }, Array.Empty<string>(), "actor-3");
        clock.AdvanceTicks(1);
        await service.NotifyRolesUpdatedAsync(user, new[] { "Editor" }, Array.Empty<string>(), "actor-3");

        Assert.Equal(2, publisher.Events.Count);
        Assert.NotNull(publisher.Events[0].Fingerprint);
        Assert.NotNull(publisher.Events[1].Fingerprint);
        Assert.NotEqual(publisher.Events[0].Fingerprint, publisher.Events[1].Fingerprint);
        Assert.StartsWith("role:user-3:", publisher.Events[0].Fingerprint);
        Assert.StartsWith("role:user-3:", publisher.Events[1].Fingerprint);
    }

    private sealed class IncrementingTestClock : IClock
    {
        private long _ticks;

        public IncrementingTestClock(DateTimeOffset initial)
        {
            _ticks = initial.UtcTicks;
        }

        public DateTimeOffset UtcNow => new DateTimeOffset(_ticks, TimeSpan.Zero);

        public void AdvanceTicks(long ticks)
        {
            if (ticks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks));
            }

            _ticks += ticks;
        }
    }
}
