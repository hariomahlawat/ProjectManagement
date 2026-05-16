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
    public async Task AssignBacklogItemToSprintAsync_WhenSprintClosed_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.Comdt);
        await service.CloseSprintAsync(sprint.Id, sprint.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedBacklogTaskAsync(db);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignBacklogItemToSprintAsync(task.Id, sprint.Id, "assignee", RoleNames.Ta, "planner", RoleNames.Comdt));
        Assert.Contains("closed sprint", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task AssignBacklogItemToSprintAsync_WhenTaskClosed_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Closed);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignBacklogItemToSprintAsync(task.Id, sprint.Id, "assignee", RoleNames.Ta, "planner", RoleNames.Comdt));
        Assert.Contains("closed tasks cannot be assigned", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task AssignBacklogItemToSprintAsync_WithNonAuthority_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignBacklogItemToSprintAsync(task.Id, sprint.Id, "assignee", RoleNames.Ta, "actor", RoleNames.ProjectOfficer));
        Assert.Contains("not authorized", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task MoveTaskToBacklogAsync_ClearsSprintAndAssigneeAndAuditsMove()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db);
        await AssignBacklogTaskToSprintAsync(db, service, task, sprint.Id, "planner", RoleNames.HoD);

        // SECTION: Act
        var backlogTask = await service.MoveTaskToBacklogAsync(task.Id, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Null(backlogTask.SprintId);
        Assert.Equal(string.Empty, backlogTask.AssignedToUserId);
        Assert.Equal(ActionTaskStatuses.Backlog, backlogTask.Status);
        Assert.Null(backlogTask.SubmittedOn);
        Assert.Null(backlogTask.ClosedOn);
        Assert.Contains(await db.ActionTaskAuditLogs.ToListAsync(), x => x.TaskId == task.Id && x.ActionType == "TaskMovedToBacklogRemoveAssignee" && x.OldValue!.Contains("Status=Assigned") && x.NewValue!.Contains("Status=Backlog"));
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
        await AssignBacklogTaskToSprintAsync(db, service, task, sprint.Id, "planner", RoleNames.HoD);

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
        await AssignBacklogTaskToSprintAsync(db, service, task, sprint.Id, "planner", RoleNames.Comdt);

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
        await AssignBacklogTaskToSprintAsync(db, service, task, source.Id, "planner", RoleNames.Comdt);

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
        await AssignBacklogTaskToSprintAsync(db, service, task, source.Id, "planner", RoleNames.Comdt);
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
    public async Task CloseSprintWithDispositionAsync_MovesSelectedTaskToBacklogAndRemovesAssignee()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.HoD);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Blocked);
        await AssignBacklogTaskToSprintAsync(db, service, task, source.Id, "planner", RoleNames.HoD);

        // SECTION: Act
        await service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, Array.Empty<int>(), null, new[] { task.Id }, "Return to backlog", "planner", RoleNames.HoD);

        // SECTION: Assert
        var movedTask = await db.ActionTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Null(movedTask.SprintId);
        Assert.Equal(string.Empty, movedTask.AssignedToUserId);
        Assert.Contains(await db.ActionTaskAuditLogs.ToListAsync(), x => x.TaskId == task.Id && x.ActionType == "TaskMovedToBacklogRemoveAssignee");
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
        await AssignBacklogTaskToSprintAsync(db, service, carriedTask, source.Id, "planner", RoleNames.Comdt);
        await AssignBacklogTaskToSprintAsync(db, service, undispositionedTask, source.Id, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { carriedTask.Id }, target.Id, Array.Empty<int>(), "Only one task dispositioned", "planner", RoleNames.Comdt));
        Assert.Contains("all unfinished tasks", ex.Message.ToLowerInvariant());
    }

    [Theory]
    [InlineData(-15, 0)]
    [InlineData(0, 14)]
    [InlineData(7, 21)]
    public async Task CloseSprintWithDispositionAsync_RejectsCarryForwardIntoOverlappingOrNonLaterSprint(int targetStartOffsetDays, int targetEndOffsetDays)
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.Comdt);
        var target = await service.CreateSprintAsync(NewSprint("Older or Same Target", targetStartOffsetDays, targetEndOffsetDays), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db);
        await AssignBacklogTaskToSprintAsync(db, service, task, source.Id, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { task.Id }, target.Id, Array.Empty<int>(), "Invalid target timing", "planner", RoleNames.Comdt));
        Assert.Contains("must start after", ex.Message.ToLowerInvariant());
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
        await AssignBacklogTaskToSprintAsync(db, service, task, source.Id, "planner", RoleNames.Comdt);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { task.Id }, target.Id, Array.Empty<int>(), "Invalid carry", "planner", RoleNames.Comdt));
        Assert.Contains("closed sprint", ex.Message.ToLowerInvariant());
    }




    [Fact]
    public async Task MoveTaskToBacklogAsync_FromSubmittedTask_ResetsStatusToBacklog()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Submitted);
        task.SubmittedOn = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await AssignBacklogTaskToSprintAsync(db, service, task, sprint.Id, "planner", RoleNames.HoD);
        task.Status = ActionTaskStatuses.Submitted;
        task.SubmittedOn = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // SECTION: Act
        var backlogTask = await service.MoveTaskToBacklogRemoveAssigneeAsync(task.Id, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Equal(ActionTaskStatuses.Backlog, backlogTask.Status);
        Assert.Null(backlogTask.SprintId);
        Assert.Equal(string.Empty, backlogTask.AssignedToUserId);
        Assert.Null(backlogTask.SubmittedOn);
    }

    [Fact]
    public async Task AssignBacklogItemToSprintAsync_BacklogItem_SetsAssigneeAndAssignedStatus()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Backlog);
        task.AssignedToUserId = string.Empty;
        task.AssignedToRole = string.Empty;
        await db.SaveChangesAsync();

        // SECTION: Act
        var sprintTask = await service.AssignBacklogItemToSprintAsync(task.Id, sprint.Id, "assignee", RoleNames.Ta, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Equal(sprint.Id, sprintTask.SprintId);
        Assert.Equal("assignee", sprintTask.AssignedToUserId);
        Assert.Equal(ActionTaskStatuses.Assigned, sprintTask.Status);
        Assert.Null(sprintTask.SubmittedOn);
        Assert.Null(sprintTask.ClosedOn);
    }

    [Fact]
    public async Task AssignOutsideSprintTaskToSprintAsync_OutsideSprintTask_RetainsAssignee()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.InProgress);

        // SECTION: Act
        var sprintTask = await service.AssignOutsideSprintTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Equal(sprint.Id, sprintTask.SprintId);
        Assert.Equal("assignee", sprintTask.AssignedToUserId);
        Assert.Equal(ActionTaskStatuses.InProgress, sprintTask.Status);
    }



    [Fact]
    public async Task AssignOutsideSprintTaskToSprintAsync_SubmittedOutsideSprintTask_RetainsStatusAndSubmittedOn()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var submittedOn = DateTime.UtcNow.AddHours(-2);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Submitted);
        task.SubmittedOn = submittedOn;
        await db.SaveChangesAsync();

        // SECTION: Act
        var sprintTask = await service.AssignOutsideSprintTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Equal(sprint.Id, sprintTask.SprintId);
        Assert.Equal("assignee", sprintTask.AssignedToUserId);
        Assert.Equal(RoleNames.Ta, sprintTask.AssignedToRole);
        Assert.Equal(ActionTaskStatuses.Submitted, sprintTask.Status);
        Assert.Equal(submittedOn, sprintTask.SubmittedOn);
    }

    [Fact]
    public async Task AssignBacklogItemToSprintAsync_OutsideSprintTask_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignBacklogItemToSprintAsync(task.Id, sprint.Id, "assignee", RoleNames.Ta, "planner", RoleNames.HoD));
        Assert.Equal("Only backlog items can be assigned to sprint through this action.", ex.Message);
    }

    [Fact]
    public async Task AssignBacklogItemToSprintAsync_AlreadySprintAssignedTask_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.HoD);
        var target = await service.CreateSprintAsync(NewSprint("Target", 15, 30), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db);
        await AssignBacklogTaskToSprintAsync(db, service, task, source.Id, "planner", RoleNames.HoD);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignBacklogItemToSprintAsync(task.Id, target.Id, "assignee", RoleNames.Ta, "planner", RoleNames.HoD));
        Assert.Equal("Only backlog items can be assigned to sprint through this action.", ex.Message);
    }

    [Fact]
    public async Task AssignOutsideSprintTaskToSprintAsync_BacklogTask_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.HoD);
        var task = await SeedBacklogTaskAsync(db);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignOutsideSprintTaskToSprintAsync(task.Id, sprint.Id, "planner", RoleNames.HoD));
        Assert.Equal("Only assigned tasks outside sprint can be added to sprint through this action.", ex.Message);
    }

    [Fact]
    public async Task AssignOutsideSprintTaskToSprintAsync_AlreadySprintAssignedTask_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.HoD);
        var target = await service.CreateSprintAsync(NewSprint("Target", 15, 30), "planner", RoleNames.HoD);
        var task = await SeedTaskAsync(db);
        await AssignBacklogTaskToSprintAsync(db, service, task, source.Id, "planner", RoleNames.HoD);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignOutsideSprintTaskToSprintAsync(task.Id, target.Id, "planner", RoleNames.HoD));
        Assert.Equal("Only assigned tasks outside sprint can be added to sprint through this action.", ex.Message);
    }

    [Fact]
    public async Task RemoveTaskFromSprintKeepAssignedAsync_OutsideSprintTask_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.InProgress);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RemoveTaskFromSprintKeepAssignedAsync(task.Id, "planner", RoleNames.HoD));
        Assert.Equal("Only sprint tasks can be removed from sprint.", ex.Message);
    }

    [Fact]
    public async Task MoveTaskToBacklogRemoveAssigneeAsync_AlreadyBacklogTask_RejectsRequest()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedBacklogTaskAsync(db);

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MoveTaskToBacklogRemoveAssigneeAsync(task.Id, "planner", RoleNames.HoD));
        Assert.Equal("Task is already in backlog.", ex.Message);
    }

    [Fact]
    public async Task CloseSprintWithDispositionAsync_InvalidSprintTaskMustReturnToBacklogOrBeCorrected()
    {
        // SECTION: Arrange invalid sprint assignment
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.Comdt);
        var target = await service.CreateSprintAsync(NewSprint("Target", 15, 29), "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned);
        task.SprintId = source.Id;
        task.AssignedToUserId = "assignee";
        task.AssignedToRole = string.Empty;
        await db.SaveChangesAsync();

        // SECTION: Act + Assert carry-forward and keep-outside dispositions are rejected for invalid sprint tasks.
        var carryEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, new[] { task.Id }, target.Id, Array.Empty<int>(), Array.Empty<int>(), "Carry invalid", "planner", RoleNames.Comdt));
        var outsideEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, Array.Empty<int>(), null, new[] { task.Id }, Array.Empty<int>(), "Keep invalid outside", "planner", RoleNames.Comdt));

        Assert.Equal("Tasks with invalid sprint assignment must be returned to backlog or corrected before sprint closure.", carryEx.Message);
        Assert.Equal("Tasks with invalid sprint assignment must be returned to backlog or corrected before sprint closure.", outsideEx.Message);
    }

    [Fact]
    public async Task CloseSprintWithDispositionAsync_BacklogDisposition_ResetsTaskToBacklogStatus()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var source = await service.CreateSprintAsync(NewSprint("Source"), "planner", RoleNames.Comdt);
        await service.ActivateSprintAsync(source.Id, source.RowVersion, "planner", RoleNames.Comdt);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Submitted);
        task.SubmittedOn = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await AssignBacklogTaskToSprintAsync(db, service, task, source.Id, "planner", RoleNames.Comdt);
        task.Status = ActionTaskStatuses.Submitted;
        task.SubmittedOn = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // SECTION: Act
        await service.CloseSprintWithDispositionAsync(source.Id, source.RowVersion, Array.Empty<int>(), null, Array.Empty<int>(), new[] { task.Id }, "Move back", "planner", RoleNames.Comdt);

        // SECTION: Assert
        var backlogTask = await db.ActionTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(ActionTaskStatuses.Backlog, backlogTask.Status);
        Assert.Null(backlogTask.SprintId);
        Assert.Equal(string.Empty, backlogTask.AssignedToUserId);
        Assert.Null(backlogTask.SubmittedOn);
    }

    [Fact]
    public async Task GetSprintAuditHistoryAsync_ReturnsLifecycleAuditEvents()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);
        await service.UpdateSprintAsync(sprint.Id, sprint.RowVersion, "Updated Sprint", "Updated goal", sprint.StartDate, sprint.EndDate, "planner", RoleNames.Comdt);

        // SECTION: Act
        var history = await service.GetSprintAuditHistoryAsync(sprint.Id);

        // SECTION: Assert
        Assert.Contains(history, x => x.ActionType == "SprintCreated");
        Assert.Contains(history, x => x.ActionType == "SprintUpdated");
        Assert.All(history, x => Assert.Equal(sprint.Id, x.SprintId));
    }


    [Fact]
    public async Task CreateSprintAsync_UsesClockUtcNowForTimestampsAndAudit()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);

        // SECTION: Act
        var sprint = await service.CreateSprintAsync(NewSprint(), "planner", RoleNames.Comdt);

        // SECTION: Assert
        var audit = await db.ActionSprintAuditLogs.SingleAsync(x => x.SprintId == sprint.Id);
        Assert.Equal(TestActionTrackerClock.FixedUtcNow, sprint.CreatedAtUtc);
        Assert.Equal(TestActionTrackerClock.FixedUtcNow, audit.PerformedAt);
    }

    // SECTION: Test helpers
    private static ActionSprintService CreateService(ApplicationDbContext db)
        => new(db, new ActionTaskPermissionService(), new ActionSprintWorkflowPolicy(), new TestActionTrackerClock());

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

    private static async Task<ActionTaskItem> AssignBacklogTaskToSprintAsync(ApplicationDbContext db, ActionSprintService service, ActionTaskItem task, int sprintId, string userId, string role)
    {
        // SECTION: Prepare backlog item source state before exercising Backlog -> Sprint movement.
        task.SprintId = null;
        task.AssignedToUserId = string.Empty;
        task.AssignedToRole = string.Empty;
        task.Status = ActionTaskStatuses.Backlog;
        task.SubmittedOn = null;
        task.ClosedOn = null;
        await db.SaveChangesAsync();

        return await service.AssignBacklogItemToSprintAsync(task.Id, sprintId, "assignee", RoleNames.Ta, userId, role);
    }

    private static async Task<ActionTaskItem> SeedBacklogTaskAsync(ApplicationDbContext db)
    {
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Backlog);
        task.AssignedToUserId = string.Empty;
        task.AssignedToRole = string.Empty;
        await db.SaveChangesAsync();
        return task;
    }

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
    private sealed class TestActionTrackerClock : IActionTrackerClock
    {
        public static readonly DateTime FixedUtcNow = new(2030, 1, 15, 6, 30, 0);

        public DateTime UtcNow => FixedUtcNow;
        public DateTime UtcToday => UtcNow.Date;
        public DateTime IstNow => FixedUtcNow.AddHours(5.5);
        public DateTime IstToday => IstNow.Date;
    }

}
