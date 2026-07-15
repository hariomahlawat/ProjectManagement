using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class WorkspaceViewModelPresentationTests
{
    [Theory]
    [InlineData(0, 0, "Your workspace is clear")]
    [InlineData(0, 1, "1 record gap requires attention")]
    [InlineData(0, 4, "4 record gaps require attention")]
    [InlineData(1, 4, "1 action requires attention")]
    [InlineData(4, 4, "4 actions require attention")]
    public void ActionHeadline_UsesCorrectPriorityAndPluralisation(int actionCount, int recordGapCount, string expected)
    {
        var vm = new ProjectOfficerWorkspaceVm
        {
            ActionQueueTotalCount = actionCount,
            RecordGapCount = recordGapCount
        };

        Assert.Equal(expected, vm.ActionHeadline);
    }

    [Fact]
    public void OperationalSummary_UsesPortfolioAndTimelineCountsWithoutCurrentStageMisstatement()
    {
        var vm = new ProjectOfficerWorkspaceVm
        {
            ActionQueueTotalCount = 4,
            RecordGapCount = 15,
            ActionSummary = new WorkspaceActionQueueSummaryVm
            {
                ProjectCount = 3,
                ConferenceDirectionCount = 1,
                TimelineCount = 2,
                ProjectUpdateCount = 1
            }
        };

        Assert.Equal(
            "Across 3 projects · 1 conference direction · 2 timeline actions · 1 overdue update · 15 record gaps",
            vm.OperationalSummary);
    }

    [Fact]
    public void FollowUpCount_CombinesOnlyVisibleReminderAndIdeaRows()
    {
        var vm = new ProjectOfficerWorkspaceVm
        {
            PersonalReminders = new[]
            {
                new WorkspaceReminderVm(),
                new WorkspaceReminderVm()
            },
            Ideas = new[]
            {
                new WorkspaceIdeaVm { NeedsUpdate = true }
            }
        };

        Assert.Equal(3, vm.FollowUpCount);
    }

    [Fact]
    public void ActivityLabels_ExposeMonitoringCoverageAndActualLastActivityType()
    {
        var activity = new ErpActivityStripVm
        {
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 14),
            ActiveWorkingDays = 2,
            MonitoredWorkingDays = 2,
            LastActiveDate = new DateOnly(2026, 7, 14),
            Days = new[]
            {
                // Deliberately unsorted: presentation labels must use calendar dates,
                // not collection insertion order.
                new ErpActivityDayVm(new DateOnly(2026, 7, 14), 1, true, true, false, string.Empty, true),
                new ErpActivityDayVm(new DateOnly(2026, 7, 13), 2, true, true, false, string.Empty, false)
            }
        };

        Assert.Equal("Monitoring available from 13 Jul 2026", activity.MonitoringAvailabilityLabel);
        Assert.Equal("Navigation or read-only use", activity.LastActivityTypeLabel);
        Assert.Equal("Last active today", activity.LastActiveLabel);
    }
}
