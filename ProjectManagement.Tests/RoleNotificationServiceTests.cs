using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
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
        var service = new RoleNotificationService(publisher, preferences, NullLogger<RoleNotificationService>.Instance);

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
        Assert.Equal("/admin/users", evt.Route);
        Assert.Contains((NotificationKind.RoleAssignmentsChanged, "user-1", null), preferences.Calls);
    }

    [Fact]
    public async Task NotifyRolesUpdatedAsync_RespectsOptOut()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService((kind, userId, _) => false);
        var service = new RoleNotificationService(publisher, preferences, NullLogger<RoleNotificationService>.Instance);

        var user = new ApplicationUser { Id = "user-2", UserName = "user.two" };

        await service.NotifyRolesUpdatedAsync(user, new[] { "Admin" }, new string[0], "actor-2");

        Assert.Empty(publisher.Events);
        Assert.Contains((NotificationKind.RoleAssignmentsChanged, "user-2", null), preferences.Calls);
    }
}
