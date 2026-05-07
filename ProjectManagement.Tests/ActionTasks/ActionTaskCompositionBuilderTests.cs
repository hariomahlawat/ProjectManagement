using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskCompositionBuilderTests
{
    [Fact]
    public void MyWorkBuilder_PreservesPrimarySectionPrecedenceAndDeduplication()
    {
        // SECTION: Arrange
        var today = new DateTime(2026, 5, 7);
        var sprint = new ActionSprint { Id = 10, Name = "Active", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(14) };
        var tasks = new[]
        {
            NewTask(1, ActionTaskStatuses.Blocked, today.AddDays(-1), sprint.Id),
            NewTask(2, ActionTaskStatuses.InProgress, today.AddDays(2), sprint.Id),
            NewTask(3, ActionTaskStatuses.Submitted, today.AddDays(3)),
            NewTask(4, ActionTaskStatuses.Assigned, today.AddDays(5)),
            NewTask(5, ActionTaskStatuses.Closed, today.AddDays(-5))
        };
        var builder = new ActionTaskMyWorkBuilder(new FixedActionTrackerClock(today));

        // SECTION: Act
        var queues = builder.Build(tasks, sprint);

        // SECTION: Assert
        Assert.Equal(new[] { 1 }, queues.ActionRequired.Select(t => t.Id));
        Assert.Equal(new[] { 2 }, queues.CurrentWork.Select(t => t.Id));
        Assert.Equal(new[] { 3 }, queues.SubmittedAwaitingClosure.Select(t => t.Id));
        Assert.Equal(new[] { 4, 5 }, queues.AllMyTasks.Select(t => t.Id));
        Assert.Equal(tasks.Select(t => t.Id).OrderBy(id => id), queues.ActionRequired.Concat(queues.CurrentWork).Concat(queues.SubmittedAwaitingClosure).Concat(queues.AllMyTasks).Select(t => t.Id).OrderBy(id => id));
        Assert.Equal(new[] { 1, 2 }, queues.ActiveSprint.Select(t => t.Id));
    }

    [Fact]
    public void CommandCentreBuilder_PreservesSummaryWordingAndDateCounts()
    {
        // SECTION: Arrange
        var today = new DateTime(2026, 5, 7);
        var tasks = new[]
        {
            NewTask(1, ActionTaskStatuses.Assigned, today.AddDays(-1), priority: "Critical"),
            NewTask(2, ActionTaskStatuses.Blocked, today.AddDays(2)),
            NewTask(3, ActionTaskStatuses.Submitted, today.AddDays(3)),
            NewTask(4, ActionTaskStatuses.Closed, today.AddDays(-2), priority: "Critical")
        };
        var builder = new ActionTaskCommandCentreBuilder(new FixedActionTrackerClock(today));

        // SECTION: Act
        var summary = builder.Build(tasks, criticalOpenCount: 1, carryForwardCandidateTasks: 2);

        // SECTION: Assert
        Assert.Equal(3, summary.ActiveCount);
        Assert.Equal(1, summary.OverdueCount);
        Assert.Equal(1, summary.BlockedCount);
        Assert.Equal(1, summary.SubmittedPendingClosureCount);
        Assert.Equal("1 overdue. 1 blocked. 1 submitted pending closure. 1 critical open. 2 carry-forward candidates.", summary.DashboardCommandFocusSummary);
        Assert.Equal("There are 3 active tasks, including 1 critical task. 1 task is overdue. 1 submitted task is pending closure.", summary.CommandSummary);
    }

    // SECTION: Test data helper keeps builder tests focused on composition behaviour.
    private static ActionTaskItem NewTask(int id, string status, DateTime dueDate, int? sprintId = null, string priority = "Normal")
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
            Priority = priority,
            Status = status,
            SprintId = sprintId,
            ClosedOn = status == ActionTaskStatuses.Closed ? dueDate : null,
            SubmittedOn = status == ActionTaskStatuses.Submitted ? dueDate.AddDays(-1) : null
        };

    private sealed class FixedActionTrackerClock : IActionTrackerClock
    {
        public FixedActionTrackerClock(DateTime today)
        {
            UtcToday = today.Date;
            UtcNow = today.Date.AddHours(12);
        }

        public DateTime UtcNow { get; }
        public DateTime UtcToday { get; }
    }
}
