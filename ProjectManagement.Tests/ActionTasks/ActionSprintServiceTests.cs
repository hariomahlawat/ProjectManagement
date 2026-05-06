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
        Assert.Contains(await db.ActionSprintAuditLogs.ToListAsync(), x => x.SprintId == sprint.Id && x.ActionType == "SprintCreated");
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
    public async Task CreateSprintAsync_WithNonAuthority_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateSprintAsync(NewSprint(), "actor", RoleNames.ProjectOfficer));
        Assert.Contains("not authorized", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task UpdateSprintAsync_WithNonAuthority_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateSprintAsync(sprint.Id, sprint.RowVersion, "Updated", "Goal", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7), "actor", RoleNames.ProjectOfficer));
        Assert.Contains("not authorized", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task ActivateSprintAsync_PlannedSprint_BecomesActive()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);

        // SECTION: Act
        var active = await service.ActivateSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Equal(ActionSprintStatus.Active, active.Status);
        Assert.NotNull(active.ActivatedAtUtc);
        Assert.Contains(await db.ActionSprintAuditLogs.ToListAsync(), x => x.SprintId == sprint.Id && x.ActionType == "SprintActivated");
    }

    [Fact]
    public async Task ActivateSprintAsync_WithNonAuthority_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateSprintAsync(sprint.Id, sprint.RowVersion, "actor", RoleNames.ProjectOfficer));
        Assert.Contains("not authorized", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task ActivateSprintAsync_WhenAnotherSprintActive_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var first = await service.CreateSprintAsync(NewSprint("Sprint 1"), "planner", RoleNames.Comdt);
        var second = await service.CreateSprintAsync(NewSprint("Sprint 2"), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(first.Id, first.RowVersion, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateSprintAsync(second.Id, second.RowVersion, "planner", RoleNames.Comdt));
        Assert.Contains("Only one active sprint", ex.Message);
    }

    [Fact]
    public async Task CloseSprintAsync_ActiveSprint_BecomesClosed()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        await service.ActivateSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.HoD);

        // SECTION: Act
        var closed = await service.CloseSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Equal(ActionSprintStatus.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAtUtc);
        Assert.Contains(await db.ActionSprintAuditLogs.ToListAsync(), x => x.SprintId == sprint.Id && x.ActionType == "SprintClosed");
    }

    [Fact]
    public async Task CloseSprintAsync_WithNonAuthority_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintAsync(sprint.Id, sprint.RowVersion, "actor", RoleNames.ProjectOfficer));
        Assert.Contains("not authorized", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task UpdateSprintAsync_WithPlanningAuthority_UpdatesAndAuditsSprint()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);

        // SECTION: Act
        var updated = await service.UpdateSprintAsync(sprint.Id, sprint.RowVersion, "Updated Sprint", "Updated goal", DateTime.UtcNow.Date.AddDays(1), DateTime.UtcNow.Date.AddDays(8), "planner", RoleNames.Comdt);

        // SECTION: Assert
        Assert.Equal("Updated Sprint", updated.Name);
        Assert.Contains(await db.ActionSprintAuditLogs.ToListAsync(), x => x.SprintId == sprint.Id && x.ActionType == "SprintUpdated" && x.OldValue != null && x.NewValue != null);
    }

    [Fact]
    public async Task UpdateSprintAsync_WithoutRowVersion_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateSprintAsync(sprint.Id, Array.Empty<byte>(), "Updated", "Goal", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7), "planner", RoleNames.Comdt));
        Assert.Contains("row version", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task UpdateSprintAsync_WithStaleRowVersion_RaisesConcurrencyConflict()
    {
        // SECTION: Arrange
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        byte[] staleRowVersion;
        int sprintId;

        await using (var setupDb = CreateDb(databaseName, databaseRoot))
        {
            var setupService = CreateService(setupDb);
            var sprint = await setupService.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
            staleRowVersion = sprint.RowVersion.ToArray();
            sprintId = sprint.Id;
        }

        await using (var currentDb = CreateDb(databaseName, databaseRoot))
        {
            var currentService = CreateService(currentDb);
            var currentSprint = await currentDb.ActionSprints.SingleAsync(x => x.Id == sprintId);
            await currentService.UpdateSprintAsync(currentSprint.Id, currentSprint.RowVersion, "Current Sprint", "Current goal", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7), "planner", RoleNames.Comdt);
        }

        await using var staleDb = CreateDb(databaseName, databaseRoot);
        var staleService = CreateService(staleDb);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<ActionTaskConcurrencyException>(() =>
            staleService.UpdateSprintAsync(sprintId, staleRowVersion, "Stale Sprint", "Stale goal", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7), "planner", RoleNames.Comdt));
        Assert.Contains("updated by another user", ex.Message);
    }

    [Fact]
    public async Task AssignTaskToSprintAsync_WhenSprintClosed_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.Comdt);
        await service.CloseSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.Comdt));
        Assert.Contains("closed sprint", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task AssignTaskToSprintAsync_WhenTaskClosed_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Closed);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.Comdt));
        Assert.Contains("closed tasks cannot be assigned", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task AssignTaskToSprintAsync_WithNonAuthority_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignTaskToSprintAsync(task.Id, sprint.Id, "actor", RoleNames.ProjectOfficer));
        Assert.Contains("not authorized", ex.Message.ToLowerInvariant());
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

    [Fact]
    public async Task MoveTaskToBacklogAsync_WhenTaskClosed_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Closed);
        task.SprintId = sprint.Id;
        await db.SaveChangesAsync();

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MoveTaskToBacklogAsync(task.Id, "planner", RoleNames.HoD));
        Assert.Contains("closed tasks cannot be moved", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task MoveTaskToBacklogAsync_WithNonAuthority_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db);
        await service.AssignTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.HoD);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MoveTaskToBacklogAsync(task.Id, "actor", RoleNames.ProjectOfficer));
        Assert.Contains("not authorized", ex.Message.ToLowerInvariant());
    }


    [Fact]
    public async Task CloseSprintAsync_WithUnfinishedTasks_RequiresClosureReview()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);
        await service.AssignTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.Comdt));
        Assert.Contains("closure review", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task CloseSprintWithDispositionAsync_CarriesForwardSelectedTaskWithoutCloningAndAuditsMove()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.Comdt);
        var target = await service.CreateSprintAsync(NewSprint("Next", 15, 30), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);
        await service.AssignTaskToSprintAsync(task.Id, source.Id, "planner", RoleNames.Comdt);

        // SECTION: Act
        await service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { task.Id }, target.Id, Array.Empty<int>(), "Continue next cycle", "planner", RoleNames.Comdt);

        // SECTION: Assert
        var movedTask = await db.ActionTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(target.Id, movedTask.SprintId);
        Assert.Equal(1, await db.ActionTasks.CountAsync());
        Assert.Contains(await db.ActionTaskAuditLogs.ToListAsync(), x => x.TaskId == task.Id && x.ActionType == "TaskCarriedForward");
        Assert.Contains(await db.ActionSprintAuditLogs.ToListAsync(), x => x.SprintId == source.Id && x.ActionType == "SprintClosed" && x.Remarks!.Contains("Carried forward: 1"));
    }

    [Fact]
    public async Task CloseSprintWithDispositionAsync_WithMaximumRemarks_KeepsAuditRemarksWithinLimit()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.Comdt);
        var target = await service.CreateSprintAsync(NewSprint("Next", 15, 30), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);
        await service.AssignTaskToSprintAsync(task.Id, source.Id, "planner", RoleNames.Comdt);
        var remarks = new string('R', 2000);

        // SECTION: Act
        await service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { task.Id }, target.Id, Array.Empty<int>(), remarks, "planner", RoleNames.Comdt);

        // SECTION: Assert
        var sprintAudit = await db.ActionSprintAuditLogs.SingleAsync(x => x.SprintId == source.Id && x.ActionType == "SprintClosed");
        var taskAudit = await db.ActionTaskAuditLogs.SingleAsync(x => x.TaskId == task.Id && x.ActionType == "TaskCarriedForward");
        Assert.NotNull(sprintAudit.Remarks);
        Assert.NotNull(taskAudit.Remarks);
        Assert.True(sprintAudit.Remarks!.Length <= 2000);
        Assert.True(taskAudit.Remarks!.Length <= 2000);
    }

    [Fact]
    public async Task CloseSprintWithDispositionAsync_MovesSelectedTaskToBacklog()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.HoD);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Blocked);
        await service.AssignTaskToSprintAsync(task.Id, source.Id, "planner", RoleNames.HoD);

        // SECTION: Act
        await service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, Array.Empty<int>(), null, new[] { task.Id }, "Return to backlog", "planner", RoleNames.HoD);

        // SECTION: Assert
        var movedTask = await db.ActionTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Null(movedTask.SprintId);
        Assert.Contains(await db.ActionTaskAuditLogs.ToListAsync(), x => x.TaskId == task.Id && x.ActionType == "TaskRemovedFromSprint");
    }

    [Fact]
    public async Task CloseSprintWithDispositionAsync_WithPartialDisposition_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.Comdt);
        var target = await service.CreateSprintAsync(NewSprint("Next", 15, 30), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.Comdt);
        var carriedTask = await SeedTaskAsync(db, ActionTaskStatuses.Assigned);
        var undispositionedTask = await SeedTaskAsync(db, ActionTaskStatuses.Blocked);
        await service.AssignTaskToSprintAsync(carriedTask.Id, source.Id, "planner", RoleNames.Comdt);
        await service.AssignTaskToSprintAsync(undispositionedTask.Id, source.Id, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { carriedTask.Id }, target.Id, Array.Empty<int>(), "Only one task dispositioned", "planner", RoleNames.Comdt));
        Assert.Contains("all unfinished tasks", ex.Message.ToLowerInvariant());
    }

    [Theory]
    [InlineData(-15, 0)]
    [InlineData(0, 14)]
    public async Task CloseSprintWithDispositionAsync_RejectsCarryForwardIntoOlderOrNonLaterSprint(int targetStartOffsetDays, int targetEndOffsetDays)
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.Comdt);
        var target = await service.CreateSprintAsync(NewSprint("Older or Same Target", targetStartOffsetDays, targetEndOffsetDays), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);
        await service.AssignTaskToSprintAsync(task.Id, source.Id, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { task.Id }, target.Id, Array.Empty<int>(), "Invalid target timing", "planner", RoleNames.Comdt));
        Assert.Contains("must be later", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task CloseSprintWithDispositionAsync_RejectsMovementIntoClosedSprint()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.Comdt);
        var target = await service.CreateSprintAsync(NewSprint("Closed Target", 15, 30), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(target.Id, target.RowVersion, "planner", RoleNames.Comdt);
        await service.CloseSprintAsync(target.Id, target.RowVersion, "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);
        await service.AssignTaskToSprintAsync(task.Id, source.Id, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { task.Id }, target.Id, Array.Empty<int>(), "Invalid carry", "planner", RoleNames.Comdt));
        Assert.Contains("closed sprint", ex.Message.ToLowerInvariant());
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

    private static async Task<ActionTaskItem> SeedTaskAsync(ApplicationDbContext db, string status = ActionTaskStatuses.Assigned)
    {
        var now = DateTime.UtcNow;
        var task = new ActionTaskItem
        {
            Title = "Task",
            Description = "Task",
            CreatedByUserId = "creator",
            AssignedToUserId = "assignee",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = now.AddDays(1),
            Priority = "Normal",
            AssignedOn = now,
            Status = status,
            ClosedOn = string.Equals(status, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase) ? now : null,
            RowVersion = [1, 2, 3]
        };

        db.ActionTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}
