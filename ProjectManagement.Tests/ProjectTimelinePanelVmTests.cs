using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTimelinePanelVmTests
{
    [Fact]
    public void ShowTimeline_PreservesWorkspaceForCompletedLegacyProjectWithoutHistory()
    {
        var panel = new ProjectTimelinePanelVm
        {
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            IsLegacy = true,
            Timeline = new TimelineVm
            {
                Items =
                [
                    new TimelineItemVm { Status = StageStatus.NotStarted }
                ]
            }
        };

        Assert.False(panel.HasRecordedStageHistory);
        Assert.True(panel.ShowTimeline);
        Assert.Equal(ProjectTimelinePanelVm.RemarksPanelName, panel.DefaultPanel);
    }

    [Fact]
    public void ShowTimeline_PreservesRecordedHistoryForCancelledLegacyProject()
    {
        var panel = new ProjectTimelinePanelVm
        {
            LifecycleStatus = ProjectLifecycleStatus.Cancelled,
            IsLegacy = true,
            Timeline = new TimelineVm
            {
                Items =
                [
                    new TimelineItemVm
                    {
                        Status = StageStatus.Completed,
                        CompletedOn = new DateOnly(2024, 3, 15)
                    }
                ]
            }
        };

        Assert.True(panel.HasRecordedStageHistory);
        Assert.True(panel.ShowTimeline);
        Assert.Equal(ProjectTimelinePanelVm.RemarksPanelName, panel.DefaultPanel);
    }

    [Fact]
    public void ShowTimeline_PreservesWorkspaceForCancelledLegacyProjectWithoutHistory()
    {
        var panel = new ProjectTimelinePanelVm
        {
            LifecycleStatus = ProjectLifecycleStatus.Cancelled,
            IsLegacy = true,
            Timeline = new TimelineVm
            {
                Items =
                [
                    new TimelineItemVm { Status = StageStatus.NotStarted }
                ]
            }
        };

        Assert.False(panel.HasRecordedStageHistory);
        Assert.True(panel.ShowTimeline);
        Assert.Equal(ProjectTimelinePanelVm.RemarksPanelName, panel.DefaultPanel);
    }

    [Fact]
    public void ShowTimeline_PreservesActiveLifecycleWorkspace()
    {
        var panel = new ProjectTimelinePanelVm
        {
            LifecycleStatus = ProjectLifecycleStatus.Active,
            IsLegacy = true,
            Timeline = new TimelineVm()
        };

        Assert.True(panel.ShowTimeline);
        Assert.Equal(ProjectTimelinePanelVm.TimelinePanelName, panel.DefaultPanel);
    }
}
