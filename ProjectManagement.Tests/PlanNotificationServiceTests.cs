using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Plans;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class PlanNotificationServiceTests
{
    [Fact]
    public async Task NotifyPlanSubmittedAsync_SendsToHodWithMetadata()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService();
        var service = new PlanNotificationService(publisher, preferences, NullLogger<PlanNotificationService>.Instance);

        var plan = new PlanVersion
        {
            Id = 10,
            ProjectId = 5,
            VersionNo = 3,
            Status = PlanVersionStatus.PendingApproval,
            OwnerUserId = "owner-1",
            SubmittedByUserId = "po-1"
        };
        var project = new Project
        {
            Id = 5,
            Name = "Alpha",
            HodUserId = "hod-1",
            LeadPoUserId = "po-1"
        };

        await service.NotifyPlanSubmittedAsync(plan, project, "po-1");

        Assert.Single(publisher.Events);
        var evt = publisher.Events[0];
        Assert.Equal(NotificationKind.PlanSubmitted, evt.Kind);
        Assert.Equal(new[] { "hod-1" }, evt.Recipients);
        Assert.Equal("Plans", evt.Module);
        Assert.Equal("PlanSubmitted", evt.EventType);
        Assert.Equal("Project", evt.ScopeType);
        Assert.Equal("5", evt.ScopeId);
        Assert.Equal("/projects/overview/5?timeline=1", evt.Route);
        Assert.Equal("po-1", evt.ActorUserId);
        Assert.Equal("Alpha plan submitted", evt.Title);
        Assert.Contains((NotificationKind.PlanSubmitted, "hod-1", 5), preferences.Calls);
    }

    [Fact]
    public async Task NotifyPlanApprovedAsync_FiltersOptedOutRecipients()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService((kind, userId, _) => !string.Equals(userId, "owner-1", StringComparison.Ordinal));
        var service = new PlanNotificationService(publisher, preferences, NullLogger<PlanNotificationService>.Instance);

        var plan = new PlanVersion
        {
            Id = 20,
            ProjectId = 8,
            VersionNo = 2,
            Status = PlanVersionStatus.Approved,
            OwnerUserId = "owner-1",
            SubmittedByUserId = "po-2"
        };
        var project = new Project
        {
            Id = 8,
            Name = "Beta",
            HodUserId = "hod-2",
            LeadPoUserId = "po-2"
        };

        await service.NotifyPlanApprovedAsync(plan, project, "hod-2");

        Assert.Single(publisher.Events);
        var evt = publisher.Events[0];
        Assert.Equal(NotificationKind.PlanApproved, evt.Kind);
        Assert.Single(evt.Recipients);
        Assert.Equal("po-2", evt.Recipients.First());
        Assert.Contains((NotificationKind.PlanApproved, "owner-1", 8), preferences.Calls);
        Assert.Contains((NotificationKind.PlanApproved, "po-2", 8), preferences.Calls);
        Assert.DoesNotContain("owner-1", evt.Recipients);
    }
}
