using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class WorkspaceDisplayHelpersTests
{
    [Fact]
    public void CurrentStageDurationLabel_IsExplicit()
    {
        var row = new WorkspaceProjectMatrixRowVm { DaysInCurrentStage = 13 };

        Assert.Equal("13 days in stage", WorkspaceDisplayHelpers.CurrentStageDurationLabel(row));
    }

    [Fact]
    public void TimelineStatusLabel_ShowsMissingPdcBeforeGenericIssue()
    {
        var row = new WorkspaceProjectMatrixRowVm
        {
            HasCurrentStageIssue = true,
            IsCurrentStagePdcMissing = true
        };

        Assert.Equal("PDC not set", WorkspaceDisplayHelpers.TimelineStatusLabel(row));
        Assert.Equal("Complete the current-stage timeline", WorkspaceDisplayHelpers.TimelineStatusDetail(row));
    }

    [Fact]
    public void TimelineStatusLabel_ShowsOverdueDurationAndPdc()
    {
        var row = new WorkspaceProjectMatrixRowVm
        {
            HasOverdueCurrentStage = true,
            CurrentStagePdc = new DateOnly(2026, 7, 1),
            DaysUntilCurrentStagePdc = -2
        };

        Assert.Equal("Overdue by 2 days", WorkspaceDisplayHelpers.TimelineStatusLabel(row));
        Assert.Equal("PDC 01 Jul 2026", WorkspaceDisplayHelpers.TimelineStatusDetail(row));
    }

    [Fact]
    public void ProjectUpdateStatus_SeparatesStateAndAge()
    {
        var row = new WorkspaceProjectMatrixRowVm
        {
            LastPoRemarkAtUtc = new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc),
            DaysSinceLastPoRemark = 16,
            UpdateStatus = "ActionRequired"
        };

        Assert.Equal("Update overdue", WorkspaceDisplayHelpers.ProjectUpdateStatusLabel(row));
        Assert.Equal("16 days since update", WorkspaceDisplayHelpers.ProjectUpdateStatusDetail(row));
    }

    [Fact]
    public void TimelineStatusLabel_DoesNotCallAnUnstartedStageOnTrack()
    {
        var row = new WorkspaceProjectMatrixRowVm
        {
            IsCurrentStageNotStarted = true
        };

        Assert.Equal("Stage not started", WorkspaceDisplayHelpers.TimelineStatusLabel(row));
        Assert.Equal("Timeline not yet active", WorkspaceDisplayHelpers.TimelineStatusDetail(row));
        Assert.Equal("neutral", WorkspaceDisplayHelpers.TimelineStatusCss(row));
    }
}
