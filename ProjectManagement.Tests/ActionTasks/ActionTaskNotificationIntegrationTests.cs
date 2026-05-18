using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Security;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.Storage;
using Xunit;

namespace ProjectManagement.Tests.ActionTasks;

public sealed class ActionTaskNotificationIntegrationTests
{
    [Fact]
    public async Task CreateTaskAsync_CallsAssignedNotificationAfterSuccessfulCreate()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var notifications = new RecordingActionTaskNotificationService();
        var service = new ActionTaskService(db, new ActionTaskPermissionService(), new TestActionTrackerClock(), notifications);

        // SECTION: Act
        var task = await service.CreateTaskAsync(NewTask());

        // SECTION: Assert
        Assert.Contains(notifications.Calls, call => call.Name == "Assigned" && call.TaskId == task.Id && call.ActorUserId == "creator");
    }

    [Theory]
    [InlineData(ActionTaskStatuses.InProgress, "StatusChanged")]
    [InlineData(ActionTaskStatuses.Blocked, "Blocked")]
    [InlineData(ActionTaskStatuses.Submitted, "Submitted")]
    public async Task AddUpdateAndMaybeChangeStatusAsync_CallsCorrectNotificationForStatusChange(string newStatus, string expectedCall)
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned);
        var notifications = new RecordingActionTaskNotificationService();
        var service = new ActionTaskCollaborationService(
            db,
            new ActionTaskPermissionService(),
            new TestUploadRootProvider(),
            new PassFileSecurityValidator(),
            new StubUrlBuilder(),
            new TestActionTrackerClock(),
            notifications);

        // SECTION: Act
        await service.AddUpdateAndMaybeChangeStatusAsync(task.Id, "Required note", newStatus, "assignee", RoleNames.Ta, Array.Empty<IFormFile>(), task.RowVersion);

        // SECTION: Assert
        Assert.Contains(notifications.Calls, call => call.Name == expectedCall && call.TaskId == task.Id && call.ActorUserId == "assignee");
        Assert.DoesNotContain(notifications.Calls, call => call.Name == "ProgressUpdated");
    }

    [Fact]
    public async Task MoveTaskToBacklogRemoveAssigneeAsync_PassesPreviousAssignee()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var sprint = new ActionSprint
        {
            Name = "Sprint A",
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(7),
            Status = ActionSprintStatus.Planned,
            RowVersion = [1, 2, 3]
        };
        db.ActionSprints.Add(sprint);
        await db.SaveChangesAsync();
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned, sprint.Id);
        var notifications = new RecordingActionTaskNotificationService();
        var service = new ActionSprintService(db, new ActionTaskPermissionService(), new ActionSprintWorkflowPolicy(), new TestActionTrackerClock(), notifications);

        // SECTION: Act
        await service.MoveTaskToBacklogRemoveAssigneeAsync(task.Id, "planner", RoleNames.HoD, "move");

        // SECTION: Assert
        Assert.Contains(notifications.Calls, call => call.Name == "MovedToBacklog" && call.TaskId == task.Id && call.PreviousAssigneeUserId == "assignee");
    }

    [Fact]
    public async Task UpdateTaskDateAndCloseTask_CallDueDateAndClosedNotifications()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var task = await SeedTaskAsync(db, ActionTaskStatuses.Assigned);
        var notifications = new RecordingActionTaskNotificationService();
        var service = new ActionTaskService(db, new ActionTaskPermissionService(), new TestActionTrackerClock(), notifications);

        // SECTION: Act
        await service.UpdateTaskDateAsync(task.Id, task.RowVersion, TestActionTrackerClock.FixedToday.AddDays(5), "planner", RoleNames.HoD);
        var updated = await db.ActionTasks.SingleAsync(x => x.Id == task.Id);
        await service.CloseTaskDirectlyAsync(updated.Id, updated.RowVersion, "done", "planner", RoleNames.HoD);

        // SECTION: Assert
        Assert.Contains(notifications.Calls, call => call.Name == "DueDateChanged" && call.TaskId == task.Id);
        Assert.Contains(notifications.Calls, call => call.Name == "Closed" && call.TaskId == task.Id && call.ClosedByCommandAuthority == true);
    }

    // SECTION: Test data helpers
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ActionTaskItem NewTask()
        => new()
        {
            Title = "Task",
            Description = "Task",
            CreatedByUserId = "creator",
            AssignedToUserId = "assignee",
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = TestActionTrackerClock.FixedToday.AddDays(1),
            Priority = "Normal",
            RowVersion = [1, 2, 3]
        };

    private static async Task<ActionTaskItem> SeedTaskAsync(ApplicationDbContext db, string status, int? sprintId = null)
    {
        var task = NewTask();
        task.Status = status;
        task.SprintId = sprintId;
        task.AssignedOn = TestActionTrackerClock.FixedToday;
        db.ActionTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    // SECTION: Test doubles
    private sealed class RecordingActionTaskNotificationService : IActionTaskNotificationService
    {
        public List<Call> Calls { get; } = new();

        public Task NotifyTaskAssignedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => Record("Assigned", task, actorUserId);

        public Task NotifyProgressUpdatedAsync(ActionTaskItem task, ActionTaskUpdate? update, string actorUserId, CancellationToken cancellationToken = default)
            => Record("ProgressUpdated", task, actorUserId);

        public Task NotifyStatusChangedAsync(ActionTaskItem task, string previousStatus, string newStatus, string actorUserId, CancellationToken cancellationToken = default)
            => Record("StatusChanged", task, actorUserId);

        public Task NotifyTaskBlockedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => Record("Blocked", task, actorUserId);

        public Task NotifySubmittedForClosureAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => Record("Submitted", task, actorUserId);

        public Task NotifyTaskClosedAsync(ActionTaskItem task, string actorUserId, bool closedByCommandAuthority, CancellationToken cancellationToken = default)
            => Record("Closed", task, actorUserId, closedByCommandAuthority: closedByCommandAuthority);

        public Task NotifyDueDateChangedAsync(ActionTaskItem task, DateTime oldDate, DateTime newDate, string actorUserId, CancellationToken cancellationToken = default)
            => Record("DueDateChanged", task, actorUserId);

        public Task NotifyMovedToBacklogAsync(ActionTaskItem task, string? previousAssigneeUserId, string actorUserId, CancellationToken cancellationToken = default)
            => Record("MovedToBacklog", task, actorUserId, previousAssigneeUserId: previousAssigneeUserId);

        public Task NotifyRemovedFromSprintAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => Record("RemovedFromSprint", task, actorUserId);

        public Task NotifyAddedToSprintAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => Record("AddedToSprint", task, actorUserId);

        private Task Record(string name, ActionTaskItem task, string actorUserId, string? previousAssigneeUserId = null, bool? closedByCommandAuthority = null)
        {
            Calls.Add(new Call(name, task.Id, actorUserId, previousAssigneeUserId, closedByCommandAuthority));
            return Task.CompletedTask;
        }
    }

    private sealed record Call(string Name, int TaskId, string ActorUserId, string? PreviousAssigneeUserId, bool? ClosedByCommandAuthority);

    private sealed class TestActionTrackerClock : IActionTrackerClock
    {
        public static readonly DateTime FixedToday = new(2030, 1, 15);
        public DateTime UtcNow => FixedToday.AddHours(12);
        public DateTime UtcToday => UtcNow.Date;
        public DateTime IstNow => UtcNow.AddHours(5.5);
        public DateTime IstToday => FixedToday;
    }

    private sealed class TestUploadRootProvider : IUploadRootProvider
    {
        public string RootPath => Path.GetTempPath();
        public string GetProjectRoot(int projectId) => RootPath;
        public string GetProjectPhotosRoot(int projectId) => RootPath;
        public string GetProjectDocumentsRoot(int projectId) => RootPath;
        public string GetProjectCommentsRoot(int projectId) => RootPath;
        public string GetProjectVideosRoot(int projectId) => RootPath;
        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => RootPath;
    }

    private sealed class PassFileSecurityValidator : IFileSecurityValidator
    {
        public void ValidateRelativePath(string relativePath) { }
        public Task<bool> IsSafeAsync(string filePath, string contentType, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class StubUrlBuilder : IProtectedFileUrlBuilder
    {
        public string CreateDownloadUrl(string storageKey, string? fileName = null, string? contentType = null, TimeSpan? lifetime = null) => storageKey;
        public string CreateInlineUrl(string storageKey, string? fileName = null, string? contentType = null, TimeSpan? lifetime = null) => storageKey;
    }
}
