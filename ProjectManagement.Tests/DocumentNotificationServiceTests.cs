using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Documents;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DocumentNotificationServiceTests
{
    [Fact]
    public async Task NotifyDocumentPublishedAsync_SendsToProjectTeam()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService();
        var service = new DocumentNotificationService(publisher, preferences, NullLogger<DocumentNotificationService>.Instance);

        var project = new Project { Id = 12, Name = "Epsilon", LeadPoUserId = "po-7", HodUserId = "hod-7" };
        var document = new ProjectDocument { Id = 30, ProjectId = 12, Title = "Spec", Status = ProjectDocumentStatus.Published, FileStamp = 1, Project = project };

        await service.NotifyDocumentPublishedAsync(document, project, "reviewer-1");

        Assert.Single(publisher.Events);
        var evt = publisher.Events[0];
        Assert.Equal(NotificationKind.DocumentPublished, evt.Kind);
        Assert.Equal(new[] { "hod-7", "po-7" }, evt.Recipients.OrderBy(x => x));
        Assert.Equal("Documents", evt.Module);
        Assert.Equal("DocumentPublished", evt.EventType);
        Assert.Equal("Document", evt.ScopeType);
        Assert.Equal("30", evt.ScopeId);
        Assert.Equal("/projects/overview?id=12&mediaTab=" + ProjectMediaTabViewModel.DocumentsKey, evt.Route);
        Assert.Contains((NotificationKind.DocumentPublished, "hod-7", 12), preferences.Calls);
        Assert.Contains((NotificationKind.DocumentPublished, "po-7", 12), preferences.Calls);
    }

    [Fact]
    public async Task NotifyDocumentArchivedAsync_ExcludesOptedOut()
    {
        var publisher = new RecordingNotificationPublisher();
        var preferences = new TestPreferenceService((kind, userId, _) => !string.Equals(userId, "hod-8", StringComparison.Ordinal));
        var service = new DocumentNotificationService(publisher, preferences, NullLogger<DocumentNotificationService>.Instance);

        var project = new Project { Id = 14, Name = "Zeta", LeadPoUserId = "po-8", HodUserId = "hod-8" };
        var document = new ProjectDocument { Id = 40, ProjectId = 14, Title = "Report", Status = ProjectDocumentStatus.SoftDeleted, FileStamp = 2, Project = project };

        await service.NotifyDocumentArchivedAsync(document, project, "actor-2");

        Assert.Single(publisher.Events);
        var evt = publisher.Events[0];
        Assert.Single(evt.Recipients);
        Assert.Equal("po-8", evt.Recipients.First());
        Assert.Contains((NotificationKind.DocumentArchived, "hod-8", 14), preferences.Calls);
    }
}
