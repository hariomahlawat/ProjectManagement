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
        var service = CreateService(db);
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
        var service = CreateService(db);
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

        var service = CreateService(staleDb);

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<ActionTaskConcurrencyException>(() =>
            service.UpdateStatusAsync(task.Id, staleRowVersion, ActionTaskStatuses.Blocked, "assignee", RoleNames.Ta, "blocked"));
    }

    [Fact]
    public async Task LogsReadAndUpdateWrite_AccessDeniedForIneligibleUser()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, "owner");

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetTaskLogsAsync(task.Id, "other", RoleNames.Ta));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStatusAsync(task.Id, task.RowVersion, ActionTaskStatuses.InProgress, "other", RoleNames.Ta));
    }


    [Fact]
    public async Task CreateBacklogItemAsync_CreatesTrueBacklogState()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);

        // SECTION: Act
        var task = await service.CreateBacklogItemAsync(new ActionTaskItem
        {
            Title = "Backlog",
            Description = "Future work",
            AssignedToUserId = "should-clear",
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = DateTime.UtcNow.AddDays(2),
            Priority = "Normal",
            SprintId = 123,
            SubmittedOn = DateTime.UtcNow,
            ClosedOn = DateTime.UtcNow
        });

        // SECTION: Assert
        Assert.Equal(ActionTaskStatuses.Backlog, task.Status);
        Assert.Null(task.SprintId);
        Assert.Equal(string.Empty, task.AssignedToUserId);
        Assert.Equal(string.Empty, task.AssignedToRole);
        Assert.Null(task.SubmittedOn);
        Assert.Null(task.ClosedOn);
    }

    [Fact]
    public async Task CreateTaskAsync_RequiresResponsiblePersonAndCreatesAssignedOutsideSprintTask()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);

        // SECTION: Act
        var task = await service.CreateTaskAsync(new ActionTaskItem
        {
            Title = "Direct",
            Description = "Assigned now",
            AssignedToUserId = "assignee",
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = DateTime.UtcNow.AddDays(2),
            Priority = "Normal",
            SprintId = 456
        });

        // SECTION: Assert
        Assert.Equal(ActionTaskStatuses.Assigned, task.Status);
        Assert.Equal("assignee", task.AssignedToUserId);
        Assert.Null(task.SprintId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTaskAsync(new ActionTaskItem
        {
            Title = "Invalid",
            Description = "No assignee",
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            DueDate = DateTime.UtcNow.AddDays(2),
            Priority = "Normal"
        }));
    }

    [Fact]
    public async Task BacklogItem_CannotBeSubmittedOrChangedThroughGenericStatusUpdate()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await service.CreateBacklogItemAsync(new ActionTaskItem
        {
            Title = "Backlog",
            Description = "Future work",
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            DueDate = DateTime.UtcNow.AddDays(2),
            Priority = "Normal"
        });

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitTaskAsync(task.Id, task.RowVersion, string.Empty, RoleNames.Ta, "submit"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateStatusAsync(task.Id, task.RowVersion, ActionTaskStatuses.InProgress, "creator", RoleNames.HoD, "start"));
    }

    [Fact]
    public async Task UpdateStatusAsync_RejectsSubmittedTargetFromGenericChangeStatus()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.InProgress, "assignee");

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStatusAsync(task.Id, task.RowVersion, ActionTaskStatuses.Submitted, "assignee", RoleNames.Ta, "ready"));
        Assert.Contains("submit for closure", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Comdt_CanChangeAssignedTaskDueDate()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, "assignee");
        var newDate = TestActionTrackerClock.FixedToday.AddDays(5);

        // SECTION: Act
        await service.UpdateTaskDateAsync(task.Id, task.RowVersion, newDate, "planner", RoleNames.Comdt);

        // SECTION: Assert
        var updated = await db.ActionTasks.SingleAsync(x => x.Id == task.Id);
        Assert.Equal(newDate, updated.DueDate.Date);
    }

    [Fact]
    public async Task HoD_CanChangeAssignedTaskDueDate()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, "assignee");
        var newDate = TestActionTrackerClock.FixedToday.AddDays(5);

        // SECTION: Act
        await service.UpdateTaskDateAsync(task.Id, task.RowVersion, newDate, "planner", RoleNames.HoD);

        // SECTION: Assert
        var updated = await db.ActionTasks.SingleAsync(x => x.Id == task.Id);
        Assert.Equal(newDate, updated.DueDate.Date);
    }

    [Fact]
    public async Task Assignee_CannotChangeOwnDueDate()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, "assignee");

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateTaskDateAsync(task.Id, task.RowVersion, TestActionTrackerClock.FixedToday.AddDays(3), "assignee", RoleNames.Ta));
        Assert.Contains("not authorized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClosedTask_DateCannotBeChanged()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Closed, "assignee");
        task.ClosedOn = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateTaskDateAsync(task.Id, task.RowVersion, TestActionTrackerClock.FixedToday.AddDays(3), "planner", RoleNames.HoD));
        Assert.Contains("Closed tasks", ex.Message);
    }

    [Fact]
    public async Task ChangeTaskDate_RejectsPastDate()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, "assignee");

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateTaskDateAsync(task.Id, task.RowVersion, TestActionTrackerClock.FixedToday.AddDays(-1), "planner", RoleNames.Comdt));
        Assert.Contains("past", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BacklogItem_ChangeDate_AuditsTargetDateChanged()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var originalTargetDate = TestActionTrackerClock.FixedToday.AddDays(2);
        var backlog = await service.CreateBacklogItemAsync(new ActionTaskItem
        {
            Title = "Backlog",
            Description = "Future work",
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            DueDate = originalTargetDate,
            Priority = "Normal"
        });
        var newDate = TestActionTrackerClock.FixedToday.AddDays(6);

        // SECTION: Act
        await service.UpdateTaskDateAsync(backlog.Id, backlog.RowVersion, newDate, "planner", RoleNames.HoD);

        // SECTION: Assert
        var logs = await db.ActionTaskAuditLogs.ToListAsync();
        Assert.Contains(logs, x => x.TaskId == backlog.Id
            && x.ActionType == "TargetDateChanged"
            && x.OldValue == originalTargetDate.ToString("yyyy-MM-dd")
            && x.NewValue == newDate.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task AssignedTask_ChangeDate_AuditsDueDateChanged()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var assigned = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, "assignee");
        var oldDate = assigned.DueDate.Date;
        var newDate = TestActionTrackerClock.FixedToday.AddDays(7);

        // SECTION: Act
        await service.UpdateTaskDateAsync(assigned.Id, assigned.RowVersion, newDate, "planner", RoleNames.Comdt);

        // SECTION: Assert
        var logs = await db.ActionTaskAuditLogs.ToListAsync();
        Assert.Contains(logs, x => x.TaskId == assigned.Id
            && x.ActionType == "DueDateChanged"
            && x.OldValue == oldDate.ToString("yyyy-MM-dd")
            && x.NewValue == newDate.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task ChangeTaskDate_EnforcesRowVersion()
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
            concurrentTask.Priority = "High";
            await concurrentDb.SaveChangesAsync();
        }

        var service = CreateService(staleDb);

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<ActionTaskConcurrencyException>(() =>
            service.UpdateTaskDateAsync(task.Id, staleRowVersion, TestActionTrackerClock.FixedToday.AddDays(8), "planner", RoleNames.HoD));
    }


    [Fact]
    public async Task CreateTaskAsync_UsesClockUtcNowForAssignedTimestampAndAudit()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var clock = new MidnightIstActionTrackerClock();
        var service = CreateService(db, clock);

        // SECTION: Act
        var task = await service.CreateTaskAsync(new ActionTaskItem
        {
            Title = "Clocked task",
            Description = "Desc",
            AssignedToUserId = "assignee",
            CreatedByUserId = "creator",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = clock.IstToday.AddDays(1),
            Priority = "High"
        });

        // SECTION: Assert
        var audit = await db.ActionTaskAuditLogs.SingleAsync(x => x.TaskId == task.Id);
        Assert.Equal(clock.UtcNow, task.AssignedOn);
        Assert.Equal(clock.UtcNow, audit.PerformedAt);
    }

    [Fact]
    public async Task UpdateTaskDateAsync_RejectsDatesBeforeIstTodayRatherThanUtcToday()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var clock = new MidnightIstActionTrackerClock();
        var service = CreateService(db, clock);
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, "assignee");

        // SECTION: Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateTaskDateAsync(task.Id, task.RowVersion, clock.UtcToday, "planner", RoleNames.Comdt));
        Assert.Contains("past", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    // SECTION: Test service helpers
    private static ActionTaskService CreateService(ApplicationDbContext db, IActionTrackerClock? clock = null)
        => new(db, new ActionTaskPermissionService(), clock ?? new TestActionTrackerClock());

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

    private sealed class MidnightIstActionTrackerClock : IActionTrackerClock
    {
        public DateTime UtcNow => new(2026, 4, 27, 19, 0, 0);
        public DateTime UtcToday => UtcNow.Date;
        public DateTime IstNow => new(2026, 4, 28, 0, 30, 0);
        public DateTime IstToday => IstNow.Date;
    }

    private sealed class TestActionTrackerClock : IActionTrackerClock
    {
        public static readonly DateTime FixedToday = new(2030, 1, 15);

        public DateTime UtcNow => FixedToday.AddHours(12);

        public DateTime UtcToday => UtcNow.Date;

        public DateTime IstNow => FixedToday.AddHours(12);

        public DateTime IstToday => FixedToday;
    }
}
