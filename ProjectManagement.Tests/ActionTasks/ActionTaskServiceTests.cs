using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskServiceTests
{
    [Fact]
    public async Task CreateAndNoOpUpdateAndInvalidTransition_AreHandled()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = new ActionTaskService(db, new ActionTaskPermissionService());
        var task = await service.CreateTaskAsync(new ActionTaskItem
        {
            Title = "Task A",
            Description = "Desc",
            AssignedToUserId = "assignee",
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = DateTime.UtcNow.AddDays(1),
            Priority = "High"
        });

        // SECTION: Act + Assert
        Assert.Equal(ActionTaskStatuses.Assigned, task.Status);

        await service.UpdateStatusAsync(task.Id, task.RowVersion, ActionTaskStatuses.Assigned, "assignee", RoleNames.Ta);
        var logsAfterNoOp = await db.ActionTaskAuditLogs.CountAsync(x => x.TaskId == task.Id);
        Assert.Equal(1, logsAfterNoOp);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStatusAsync(task.Id, task.RowVersion, ActionTaskStatuses.Closed, "assignee", RoleNames.Ta));
        Assert.Contains("Use the close action", ex.Message);
    }

    [Fact]
    public async Task RemarksAndLifecycleRules_AreEnforced()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = new ActionTaskService(db, new ActionTaskPermissionService());
        var task = await SeedTaskAsync(db, ActionTaskStatuses.InProgress, "assignee");

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStatusAsync(task.Id, task.RowVersion, ActionTaskStatuses.Blocked, "assignee", RoleNames.Ta, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitTaskAsync(task.Id, task.RowVersion, "other-user", RoleNames.Ta, "submit"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseTaskAsync(task.Id, task.RowVersion, "cmd", RoleNames.Comdt, "close"));

        await service.SubmitTaskAsync(task.Id, task.RowVersion, "assignee", RoleNames.Ta, "ready");
        var submitted = await service.GetTaskAsync(task.Id);
        Assert.Equal(ActionTaskStatuses.Submitted, submitted!.Status);

        var resubmitEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitTaskAsync(task.Id, submitted.RowVersion, "assignee", RoleNames.Ta, "again"));
        Assert.Contains("Only assigned, in-progress, or blocked tasks can be submitted.", resubmitEx.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CloseTaskAsync(task.Id, submitted.RowVersion, "non-cmd", RoleNames.Ta, "close"));
    }

    [Fact]
    public async Task UpdateStatusAsync_WithStaleRowVersion_ThrowsConcurrencyException()
    {
        // SECTION: Arrange
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var staleDb = CreateDb(databaseName, databaseRoot);
        var task = await SeedTaskAsync(staleDb, ActionTaskStatuses.Assigned, "assignee");
        var staleRowVersion = task.RowVersion.ToArray();
        staleDb.ChangeTracker.Clear();
        _ = await staleDb.ActionTasks.SingleAsync(x => x.Id == task.Id);

        await using (var concurrentDb = CreateDb(databaseName, databaseRoot))
        {
            var concurrentTask = await concurrentDb.ActionTasks.SingleAsync(x => x.Id == task.Id);
            concurrentTask.RowVersion = [9, 9, 9];
            concurrentTask.Status = ActionTaskStatuses.InProgress;
            await concurrentDb.SaveChangesAsync();
        }

        var service = new ActionTaskService(staleDb, new ActionTaskPermissionService());

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<ActionTaskConcurrencyException>(() =>
            service.UpdateStatusAsync(task.Id, staleRowVersion, ActionTaskStatuses.Blocked, "assignee", RoleNames.Ta, "blocked"));
    }

    [Fact]
    public async Task LogsReadAndUpdateWrite_AccessDeniedForIneligibleUser()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = new ActionTaskService(db, new ActionTaskPermissionService());
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, "owner");

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetTaskLogsAsync(task.Id, "other", RoleNames.Ta));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStatusAsync(task.Id, task.RowVersion, ActionTaskStatuses.InProgress, "other", RoleNames.Ta));
    }

    private static ApplicationDbContext CreateDb()
        => CreateDb(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot());

    private static ApplicationDbContext CreateDb(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<ActionTaskItem> SeedTaskAsync(ApplicationDbContext db, string status, string assignee)
    {
        var task = new ActionTaskItem
        {
            Title = "Task",
            Description = "Task",
            CreatedByUserId = "creator",
            AssignedToUserId = assignee,
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = DateTime.UtcNow.AddDays(1),
            Priority = "Normal",
            AssignedOn = DateTime.UtcNow,
            Status = status,
            RowVersion = [1, 2, 3]
        };

        db.ActionTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}
