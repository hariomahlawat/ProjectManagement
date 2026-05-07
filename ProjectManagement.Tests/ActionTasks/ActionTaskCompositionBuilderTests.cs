using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskCompositionBuilderTests
{
    [Fact]
    public void MyWorkQueueBuilder_PreservesSectionPrecedenceAndDeduplication()
    {
        // SECTION: Arrange
        var today = new DateTime(2026, 5, 7);
        var builder = new ActionTaskMyWorkQueueBuilder(new FixedActionTrackerClock(today));
        var activeSprint = new ActionSprint { Id = 10, Name = "Sprint" };
        var tasks = new List<ActionTaskItem>
        {
            CreateTask(1, "Overdue in progress", ActionTaskStatuses.InProgress, today.AddDays(-1), sprintId: 10),
            CreateTask(2, "Later in progress", ActionTaskStatuses.InProgress, today.AddDays(2), sprintId: 10),
            CreateTask(3, "Submitted", ActionTaskStatuses.Submitted, today.AddDays(3), submittedOn: today.AddDays(-1)),
            CreateTask(4, "Assigned later", ActionTaskStatuses.Assigned, today.AddDays(4))
        };

        // SECTION: Act
        var queue = builder.Build(tasks, activeSprint);

        // SECTION: Assert
        Assert.Equal(new[] { "Overdue in progress" }, queue.ActionRequiredTasks.Select(t => t.Title));
        Assert.Equal(new[] { "Later in progress" }, queue.CurrentWorkTasks.Select(t => t.Title));
        Assert.Equal(new[] { "Submitted" }, queue.SubmittedAwaitingClosureTasks.Select(t => t.Title));
        Assert.Equal(new[] { "Assigned later" }, queue.AllMyTasks.Select(t => t.Title));
        Assert.Equal(new[] { "Overdue in progress", "Later in progress" }, queue.ActiveSprintTasks.Select(t => t.Title));
    }

    [Fact]
    public void CommandCentreSummaryBuilder_PreservesDashboardNarrativeAndAttentionOrdering()
    {
        // SECTION: Arrange
        var today = new DateTime(2026, 5, 7);
        var builder = new ActionTaskCommandCentreSummaryBuilder(new FixedActionTrackerClock(today));
        var tasks = new List<ActionTaskItem>
        {
            CreateTask(1, "Critical overdue", ActionTaskStatuses.Assigned, today.AddDays(-2), priority: "Critical"),
            CreateTask(2, "Blocked critical", ActionTaskStatuses.Blocked, today.AddDays(2), priority: "Critical"),
            CreateTask(3, "Submitted", ActionTaskStatuses.Submitted, today.AddDays(3)),
            CreateTask(4, "Closed", ActionTaskStatuses.Closed, today.AddDays(-3), priority: "Critical")
        };

        // SECTION: Act
        var summary = builder.Build(tasks, criticalOpenCount: 2, carryForwardCandidateTasks: 1);

        // SECTION: Assert
        Assert.Equal(3, summary.ActiveCount);
        Assert.Equal(1, summary.OverdueCount);
        Assert.Equal(1, summary.BlockedCount);
        Assert.Equal(1, summary.SubmittedCount);
        Assert.Equal("1 overdue. 1 blocked. 1 submitted pending closure. 2 critical open. 1 carry-forward candidates.", summary.DashboardCommandFocusSummary);
        Assert.Equal("There are 3 active tasks, including 2 critical tasks. 1 task is overdue. 1 submitted task is pending closure.", summary.CommandSummary);
        Assert.Equal(new[] { "Critical overdue", "Blocked critical", "Submitted" }, summary.TopAttentionTasks.Select(t => t.Title));
    }

    // SECTION: Shared Action Tracker task fixture keeps builder tests focused on composition rules.
    private static ActionTaskItem CreateTask(int id, string title, string status, DateTime dueDate, string priority = "Normal", DateTime? submittedOn = null, int? sprintId = null)
        => new()
        {
            Id = id,
            Title = title,
            Description = "Description",
            CreatedByUserId = "creator",
            AssignedToUserId = "assignee",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = dueDate,
            Priority = priority,
            Status = status,
            SubmittedOn = submittedOn,
            SprintId = sprintId
        };

    private sealed class FixedActionTrackerClock : IActionTrackerClock
    {
        public FixedActionTrackerClock(DateTime today)
        {
            UtcToday = today.Date;
            UtcNow = UtcToday.AddHours(12);
        }

        public DateTime UtcNow { get; }
        public DateTime UtcToday { get; }
    }
}
