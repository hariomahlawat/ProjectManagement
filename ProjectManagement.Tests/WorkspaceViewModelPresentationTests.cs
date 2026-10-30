using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class WorkspaceViewModelPresentationTests
{
    [Fact]
    public void FollowUpCount_CountsActionableRemindersOnly()
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

        Assert.Equal(2, vm.FollowUpCount);
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
    [Fact]
    public void HistoricalAuditDays_AreExcludedFromMonitoredActivityMetrics()
    {
        var activity = new ErpActivityStripVm
        {
            StartDate = new DateOnly(2026, 7, 12),
            EndDate = new DateOnly(2026, 7, 15),
            Days = new[]
            {
                new ErpActivityDayVm(new DateOnly(2026, 7, 12), 0, false, false, true, string.Empty, false),
                new ErpActivityDayVm(new DateOnly(2026, 7, 14), 2, true, true, false, string.Empty, false),
                new ErpActivityDayVm(new DateOnly(2026, 7, 15), 1, true, true, false, string.Empty, true)
            },
            ActiveWorkingDays = 2,
            MonitoredWorkingDays = 2,
            LastActiveDate = new DateOnly(2026, 7, 15)
        };

        Assert.Equal(1, activity.HistoricalAuditDays);
        Assert.Equal(0, activity.OperationalActionDays);
        Assert.Equal("Navigation or read-only use", activity.LastActivityTypeLabel);
        Assert.Equal("not-monitored", activity.Days[0].CellStateClass);
        Assert.False(activity.Days[0].HasActivity);
    }

}
