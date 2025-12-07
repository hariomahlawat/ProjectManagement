using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class StageNotificationServiceTests
{
    [Fact]
    public async Task NotifyStageStatusChangedAsync_SendsToProjectTeam()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService();
        var service = new StageNotificationService(publisher, preferences, NullLogger<StageNotificationService>.Instance);

        var project = new Project { Id = 7, Name = "Gamma", LeadPoUserId = "po-3", HodUserId = "hod-3" };
        var stage = new ProjectStage { Id = 15, ProjectId = 7, Project = project, StageCode = "AON", Status = StageStatus.InProgress };

        await service.NotifyStageStatusChangedAsync(stage, project, StageStatus.NotStarted, "tester");

        Assert.Single(publisher.Events);
        var evt = publisher.Events[0];
        Assert.Equal(NotificationKind.StageStatusChanged, evt.Kind);
        Assert.Equal(new[] { "po-3", "hod-3" }, evt.Recipients.OrderBy(x => x));
        Assert.Equal("Stages", evt.Module);
        Assert.Equal("StageStatusChanged", evt.EventType);
        Assert.Equal("Stage", evt.ScopeType);
        Assert.Equal("7:AON", evt.ScopeId);
        Assert.Equal("/projects/overview/7?timeline-stage=AON#timeline", evt.Route);
        Assert.Contains((NotificationKind.StageStatusChanged, "po-3", 7), preferences.Calls);
        Assert.Contains((NotificationKind.StageStatusChanged, "hod-3", 7), preferences.Calls);
    }

    [Fact]
    public async Task NotifyStageStatusChangedAsync_SkipsWhenRecipientsOptOut()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService((kind, userId, _) => !string.Equals(userId, "po-4", StringComparison.Ordinal));
        var service = new StageNotificationService(publisher, preferences, NullLogger<StageNotificationService>.Instance);

        var project = new Project { Id = 9, Name = "Delta", LeadPoUserId = "po-4", HodUserId = "hod-4" };
        var stage = new ProjectStage { Id = 25, ProjectId = 9, Project = project, StageCode = "FS", Status = StageStatus.InProgress };

        await service.NotifyStageStatusChangedAsync(stage, project, StageStatus.NotStarted, "tester");

        Assert.Single(publisher.Events);
        var evt = publisher.Events[0];
        Assert.Single(evt.Recipients);
        Assert.Equal("hod-4", evt.Recipients.First());
        Assert.DoesNotContain("po-4", evt.Recipients);
    }
}
