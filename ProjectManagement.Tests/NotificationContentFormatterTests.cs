using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NotificationContentFormatterTests
{
    [Fact]
    public void Format_DocumentGeneratedName_PreservesReadableReference()
    {
        var result = NotificationContentFormatter.Format(
            NotificationKind.DocumentPublished,
            "Documents",
            "DocumentPublished",
            "71",
            "VR Based Urban Warfare Sml",
            "Document published",
            "noting sheets - 1-3676667439909_127133026_2_2026.pdf");

        Assert.Equal("Document published", result.Title);
        Assert.Equal("Noting sheets – 1 · 127133026/2/2026", result.Summary);
        Assert.Equal(
            "noting sheets - 1-3676667439909_127133026_2_2026.pdf",
            result.SummaryTooltip);
    }

    [Fact]
    public void Format_ProjectOfficerChanged_UsesSemanticSubjectAndPreview()
    {
        var result = NotificationContentFormatter.Format(
            NotificationKind.ProjectAssignmentChanged,
            "Projects",
            "ProjectOfficerAssignmentChanged",
            "10",
            "Transit Hub",
            "Transit Hub project officer updated",
            "Project officer assignment changed from Olivia Officer to Noah Officer. Review the project overview for details.");

        Assert.Equal("Project officer changed", result.Title);
        Assert.Equal("Changed from Olivia Officer to Noah Officer.", result.Summary);
    }

    [Fact]
    public void Format_ProjectOfficerAssigned_PreservesNewProducerText()
    {
        var result = NotificationContentFormatter.Format(
            NotificationKind.ProjectAssignmentChanged,
            "Projects",
            "ProjectOfficerAssignmentChanged",
            "10",
            "Transit Hub",
            "Project officer assigned",
            "Assigned to Noah Officer.");

        Assert.Equal("Project officer assigned", result.Title);
        Assert.Equal("Assigned to Noah Officer.", result.Summary);
    }

    [Fact]
    public void Format_StageStatus_UsesFriendlyStatusWords()
    {
        var result = NotificationContentFormatter.Format(
            NotificationKind.StageStatusChanged,
            "Stages",
            "StageStatusChanged",
            "7:TEC",
            "Project AURA",
            "Project AURA stage TEC Completed",
            "Stage TEC moved from NotStarted to Completed.");

        Assert.Equal("TEC stage completed", result.Title);
        Assert.Equal("Status changed from Not started to Completed.", result.Summary);
    }
}
