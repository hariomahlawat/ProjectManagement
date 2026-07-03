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
            ActionQueueTotalCount = 5,
            ProjectsNeedingAttentionCount = 3,
            ProjectTimelineIssueCount = 2,
            AotsUnreadCount = 1,
            RecordGapCount = 15
        };

        Assert.Equal(
            "3 projects affected · 2 timeline actions pending · 15 record gaps",
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
                new WorkspaceIdeaVm()
            }
        };

        Assert.Equal(3, vm.FollowUpCount);
    }
}
