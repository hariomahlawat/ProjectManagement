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
            NewTask(5, null, ActionTaskStatuses.Assigned, today.AddDays(3))
        };
        var service = new ActionTaskQueryService();

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
        Assert.Equal(1, model.SprintReadModel.ClosureReview.CompletedTasks.Count);
        Assert.Equal(3, model.SprintReadModel.ClosureReview.UnfinishedTasks.Count);
        Assert.Equal(nextSprint.Id, model.SprintReadModel.ClosureReview.TargetSprintOptions.Single().Id);
    }

    // SECTION: Test data helper
    private static ActionTaskItem NewTask(int id, int? sprintId, string status, DateTime dueDate)
        => new()
        {
            Id = id,
            Title = $"Task {id}",
            Description = "Task",
            CreatedByUserId = "creator",
            AssignedToUserId = "assignee",
            CreatedByRole = "HoD",
            AssignedToRole = "TA",
            AssignedOn = dueDate.AddDays(-2),
            DueDate = dueDate,
            Priority = "Normal",
            Status = status,
            SprintId = sprintId,
            ClosedOn = status == ActionTaskStatuses.Closed ? dueDate : null,
            SubmittedOn = status == ActionTaskStatuses.Submitted ? dueDate.AddDays(-1) : null
        };
}
