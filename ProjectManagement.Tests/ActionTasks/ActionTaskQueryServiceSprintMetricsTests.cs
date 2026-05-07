using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskQueryServiceSprintMetricsTests
{
    [Fact]
    public void BuildReadModel_ComputesActiveSprintDashboardMetricsAndClosureReview()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var activeSprint = new ActionSprint { Id = 7, Name = "Active Ops", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(14) };
        var nextSprint = new ActionSprint { Id = 8, Name = "Next Ops", Status = ActionSprintStatus.Planned, StartDate = today.AddDays(15), EndDate = today.AddDays(30) };
        var olderSprint = new ActionSprint { Id = 9, Name = "Older Ops", Status = ActionSprintStatus.Planned, StartDate = today.AddDays(-15), EndDate = today.AddDays(-1) };
        var sameStartSprint = new ActionSprint { Id = 10, Name = "Same Start Ops", Status = ActionSprintStatus.Planned, StartDate = today, EndDate = today.AddDays(7) };
        var closedLaterSprint = new ActionSprint { Id = 11, Name = "Closed Later Ops", Status = ActionSprintStatus.Closed, StartDate = today.AddDays(31), EndDate = today.AddDays(45) };
        var tasks = new[]
        {
            NewTask(1, activeSprint.Id, ActionTaskStatuses.Closed, today.AddDays(-1)),
            NewTask(2, activeSprint.Id, ActionTaskStatuses.InProgress, today.AddDays(1)),
            NewTask(3, activeSprint.Id, ActionTaskStatuses.Blocked, today.AddDays(-2)),
            NewTask(4, activeSprint.Id, ActionTaskStatuses.Submitted, today.AddDays(2)),
            NewTask(5, null, ActionTaskStatuses.Assigned, today.AddDays(3)),
            NewTask(6, null, ActionTaskStatuses.Closed, today.AddDays(4))
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, activeSprint.Id, new[] { activeSprint, nextSprint, olderSprint, sameStartSprint, closedLaterSprint }, null, null, null, null, null, null, null),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal("Active Ops", model.ActiveSprintMetrics.ActiveSprintName);
        Assert.Equal(4, model.ActiveSprintMetrics.TotalTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.CompletedTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.InProgressTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.BlockedTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.OverdueTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.BacklogTasks);
        Assert.Equal(3, model.ActiveSprintMetrics.CarryForwardCandidateTasks);
        Assert.Equal(new[] { 5 }, model.BacklogTasks.Select(t => t.Id));
        Assert.Equal(new[] { 5 }, model.SprintReadModel.BacklogTasks.Select(t => t.Id));
        Assert.Equal(1, model.SprintReadModel.ClosureReview.CompletedTasks.Count);
        Assert.Equal(3, model.SprintReadModel.ClosureReview.UnfinishedTasks.Count);
        Assert.Equal(nextSprint.Id, model.SprintReadModel.ClosureReview.TargetSprintOptions.Single().Id);
    }


    [Fact]
    public void BuildReadModel_AppliesReportsFiltersAcrossAnalyticalSections()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 21, Name = "Filtered Sprint", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var otherSprint = new ActionSprint { Id = 22, Name = "Other Sprint", Status = ActionSprintStatus.Planned, StartDate = today.AddDays(8), EndDate = today.AddDays(14) };
        var tasks = new[]
        {
            NewTask(11, sprint.Id, ActionTaskStatuses.Blocked, today.AddDays(2), "High", "assignee", today.AddDays(-9)),
            NewTask(12, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(4), "Normal", "assignee", today.AddDays(-1)),
            NewTask(13, otherSprint.Id, ActionTaskStatuses.Blocked, today.AddDays(2), "High", "other", today.AddDays(-16)),
            NewTask(14, null, ActionTaskStatuses.Assigned, today.AddDays(2), "High", "assignee", today.AddDays(-5))
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint, otherSprint }, null, null, null, null, null, null, null, sprint.Id, "assignee", today, today.AddDays(3), ActionTaskStatuses.Blocked, "High"),
            new Dictionary<string, string> { ["assignee"] = "Responsible One", ["other"] = "Other Person" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal(4, model.Reports.TotalTaskCount);
        Assert.Equal(1, model.Reports.FilteredTaskCount);
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary("Responsible One", 1) }, model.Reports.AssigneePendingCounts);
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary("High", 1) }, model.Reports.PriorityCounts);
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary(ActionTaskStatuses.Blocked, 1) }, model.Reports.StatusCounts);
        Assert.Equal(1, model.Reports.BlockedAgeingBuckets.Single(x => x.Name == "8 to 14 days").Count);
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary("Filtered Sprint", 1) }, model.Reports.CarryForwardBySprint);
    }

    [Fact]
    public void BuildReadModel_ReportBacklogSprintFilter_RendersBacklogAgeingOnly()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 31, Name = "Sprint Scope", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var tasks = new[]
        {
            NewTask(21, null, ActionTaskStatuses.Assigned, today.AddDays(2), "Normal", "assignee", today.AddDays(-5)),
            NewTask(22, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(2), "Normal", "assignee", today.AddDays(-12)),
            NewTask(23, null, ActionTaskStatuses.Closed, today.AddDays(2), "Normal", "assignee", today.AddDays(-20))
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null, 0),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal(1, model.Reports.FilteredTaskCount);
        Assert.DoesNotContain(model.Reports.StatusCounts, item => string.Equals(item.Name, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, model.Reports.BacklogAgeingBuckets.Single(x => x.Name == "4 to 7 days").Count);
        Assert.Equal(0, model.Reports.BacklogAgeingBuckets.Single(x => x.Name == "15+ days").Count);
        Assert.All(model.Reports.CarryForwardBySprint, item => Assert.Equal(0, item.Count));
    }

    // SECTION: Test query service helper
    private static ActionTaskQueryService CreateQueryService()
    {
        var clock = new SystemActionTrackerClock();
        return new ActionTaskQueryService(clock, new ActionTaskReportBuilder(clock));
    }

    // SECTION: Test data helper
    private static ActionTaskItem NewTask(int id, int? sprintId, string status, DateTime dueDate, string priority = "Normal", string assignedToUserId = "assignee", DateTime? assignedOn = null)
        => new()
        {
            Id = id,
            Title = $"Task {id}",
            Description = "Task",
            CreatedByUserId = "creator",
            AssignedToUserId = assignedToUserId,
            CreatedByRole = "HoD",
            AssignedToRole = "TA",
            AssignedOn = assignedOn ?? dueDate.AddDays(-2),
            DueDate = dueDate,
            Priority = priority,
            Status = status,
            SprintId = sprintId,
            ClosedOn = status == ActionTaskStatuses.Closed ? dueDate : null,
            SubmittedOn = status == ActionTaskStatuses.Submitted ? dueDate.AddDays(-1) : null
        };
}
