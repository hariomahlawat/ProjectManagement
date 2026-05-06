using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionSprintServiceTests
{
    [Fact]
    public async Task CreateSprintAsync_WithPlanningAuthority_CreatesPlannedSprint()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);

        // SECTION: Act
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);

        // SECTION: Assert
        Assert.True(sprint.Id > 0);
        Assert.Equal(ActionSprintStatus.Planned, sprint.Status);
        Assert.Equal("planner", sprint.CreatedByUserId);
    }

    [Fact]
    public async Task CreateSprintAsync_WithInvalidDateRange_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = NewSprint(startOffsetDays: 5, endOffsetDays: 1);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateSprintAsync(sprint, "planner", RoleNames.HoD));
        Assert.Contains("start date", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task ActivateSprintAsync_PlannedSprint_BecomesActive()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);

        // SECTION: Act
        var active = await service.ActivateSprintAsync(sprint.Id, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Equal(ActionSprintStatus.Active, active.Status);
        Assert.NotNull(active.ActivatedAtUtc);
    }

    [Fact]
    public async Task ActivateSprintAsync_WhenAnotherSprintActive_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var first = await service.CreateSprintAsync(NewSprint("Sprint 1"), "planner", RoleNames.Comdt);
        var second = await service.CreateSprintAsync(NewSprint("Sprint 2"), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(first.Id, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateSprintAsync(second.Id, "planner", RoleNames.Comdt));
        Assert.Contains("Only one active sprint", ex.Message);
    }

    [Fact]
    public async Task CloseSprintAsync_ActiveSprint_BecomesClosed()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        await service.ActivateSprintAsync(sprint.Id, "planner", RoleNames.HoD);

        // SECTION: Act
        var closed = await service.CloseSprintAsync(sprint.Id, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Equal(ActionSprintStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAtUtc);
    }

    [Fact]
    public async Task AssignTaskToSprintAsync_WhenSprintClosed_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(sprint.Id, "planner", RoleNames.Comdt);
        await service.CloseSprintAsync(sprint.Id, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.Comdt));
        Assert.Contains("closed sprint", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task MoveTaskToBacklogAsync_ClearsSprintIdAndAuditsMove()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db);
        await service.AssignTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.HoD);

        // SECTION: Act
        var backlogTask = await service.MoveTaskToBacklogAsync(task.Id, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Null(backlogTask.SprintId);
        Assert.Contains(await db.ActionTaskAuditLogs.ToListAsync(), x => x.TaskId == task.Id && x.ActionType == "TaskMovedToBacklog");
    }

    // SECTION: Test helpers
    private static ActionSprintService CreateService(ApplicationDbContext db)
        => new(db, new ActionTaskPermissionService(), new ActionSprintWorkflowPolicy());

    private static ApplicationDbContext CreateDb()
        => CreateDb(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot());

    private static ApplicationDbContext CreateDb(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ActionSprint NewSprint(string name = "Sprint A", int startOffsetDays = 0, int endOffsetDays = 14)
        => new()
        {
            Name = name,
            Goal = "Deliver sprint goal",
            StartDate = DateTime.UtcNow.Date.AddDays(startOffsetDays),
            EndDate = DateTime.UtcNow.Date.AddDays(endOffsetDays)
        };

    private static async Task<ActionTaskItem> SeedTaskAsync(ApplicationDbContext db)
    {
        var task = new ActionTaskItem
        {
            Title = "Task",
            Description = "Task",
            CreatedByUserId = "creator",
            AssignedToUserId = "assignee",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = DateTime.UtcNow.AddDays(1),
            Priority = "Normal",
            AssignedOn = DateTime.UtcNow,
            Status = ActionTaskStatuses.Assigned,
            RowVersion = [1, 2, 3]
        };

        db.ActionTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}
