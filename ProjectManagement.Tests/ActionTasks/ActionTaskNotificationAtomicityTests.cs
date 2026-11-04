using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Security;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.Storage;
using Xunit;

namespace ProjectManagement.Tests.ActionTasks;

/// <summary>
/// Action Tracker notifications are written in the same transaction as the change they describe.
/// These run on SQLite, which has real transactions and savepoints (the in-memory provider has
/// neither): a notification commits with its change, a failing notification never undoes it, and
/// an interrupted change leaves neither. PostgreSQL's abort-on-error behaviour is covered by
/// <see cref="TransactionalNotificationQueuePostgresIntegrationTests"/>.
/// </summary>
public sealed class ActionTaskNotificationAtomicityTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _uploadRoot = Path.Combine(Path.GetTempPath(), $"prism-action-task-{Guid.NewGuid():N}");
    private DbContextOptions<ApplicationDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        // SQLite enforces the user foreign keys the in-memory provider ignores.
        foreach (var userId in new[] { "creator", "assignee" })
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, NormalizedUserName = userId.ToUpperInvariant() });
        }

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        if (Directory.Exists(_uploadRoot))
        {
            Directory.Delete(_uploadRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateTask_CommitsWithItsNotification_AndSurvivesANotificationFailure(bool failNotification)
    {
        await using var db = new ApplicationDbContext(_options);
        var notifier = new QueueingNotifier(db, failNotification);
        var service = new ActionTaskService(db, new ActionTaskPermissionService(), new TestClock(), notifier);

        var task = await service.CreateTaskAsync(NewTask());

        Assert.True(task.Id > 0);
        Assert.Equal(1, notifier.Attempts);
        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(1, await verify.ActionTasks.CountAsync());
        Assert.Equal(1, await verify.ActionTaskAuditLogs.CountAsync());
        Assert.Equal(failNotification ? 0 : 1, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task CreateTask_InterruptedWhileNotifying_LeavesNeitherTheTaskNorItsNotification()
    {
        // Publishing after the save (the old design) left a committed task whose assignee was
        // never told about it; now the two roll back together.
        await using var db = new ApplicationDbContext(_options);
        using var cancellation = new CancellationTokenSource();
        var service = new ActionTaskService(db, new ActionTaskPermissionService(), new TestClock(), new CancellingNotifier(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CreateTaskAsync(NewTask(), cancellation.Token));

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(0, await verify.ActionTasks.CountAsync());
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddUpdateWithAttachment_CommitsWithItsNotification_AndKeepsTheFileOnANotificationFailure(bool failNotification)
    {
        var taskId = await SeedTaskAsync();
        await using var db = new ApplicationDbContext(_options);
        var notifier = new QueueingNotifier(db, failNotification);

        await CreateCollaborationService(db, notifier).AddUpdateAsync(
            taskId, "Progress note", ActionTaskUpdateTypes.Progress, "assignee", RoleNames.Ta, new[] { TextFile() });

        await using var verify = new ApplicationDbContext(_options);
        Assert.Equal(1, await verify.ActionTaskUpdates.CountAsync());
        var attachment = await verify.ActionTaskAttachments.SingleAsync();
        Assert.True(File.Exists(StoredPath(attachment)));
        Assert.Equal(failNotification ? 0 : 1, await verify.NotificationDispatches.CountAsync());
    }

    [Fact]
    public async Task AddUpdateWithAttachment_InterruptedWhileNotifying_NeverLeavesAnAttachmentWithoutItsFile()
    {
        // The notification used to run after the commit, inside the block that deletes the
        // uploaded files on failure. A cancellation there deleted files whose attachment rows
        // had already committed. Now the notification is part of the transaction, so the
        // update, its attachment, its file and its notification are kept or discarded together.
        var taskId = await SeedTaskAsync();
        await using var db = new ApplicationDbContext(_options);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateCollaborationService(db, new CancellingNotifier(cancellation)).AddUpdateAsync(
                taskId, "Progress note", ActionTaskUpdateTypes.Progress, "assignee", RoleNames.Ta, new[] { TextFile() }, cancellation.Token));

        await using var verify = new ApplicationDbContext(_options);
        foreach (var attachment in await verify.ActionTaskAttachments.ToListAsync())
        {
            Assert.True(File.Exists(StoredPath(attachment)), $"Attachment {attachment.Id} is committed but its file was deleted.");
        }

        Assert.Equal(0, await verify.ActionTaskUpdates.CountAsync());
        Assert.Equal(0, await verify.ActionTaskAttachments.CountAsync());
        Assert.Equal(0, await verify.NotificationDispatches.CountAsync());
        Assert.Empty(Directory.Exists(_uploadRoot)
            ? Directory.EnumerateFiles(_uploadRoot, "*", SearchOption.AllDirectories)
            : Enumerable.Empty<string>());
    }

    private ActionTaskCollaborationService CreateCollaborationService(ApplicationDbContext db, IActionTaskNotificationService notifier)
        => new(
            db,
            new ActionTaskPermissionService(),
            new TestUploadRootProvider(_uploadRoot),
            new PassFileSecurityValidator(),
            new StubUrlBuilder(),
            new TestClock(),
            notifier);

    private async Task<int> SeedTaskAsync()
    {
        await using var db = new ApplicationDbContext(_options);
        var task = NewTask();
        task.AssignedOn = TestClock.FixedUtc;
        db.ActionTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private string StoredPath(ActionTaskAttachment attachment)
        => Path.Combine(_uploadRoot, attachment.StorageKey.Replace('/', Path.DirectorySeparatorChar));

    private static ActionTaskItem NewTask() => new()
    {
        Title = "Prepare the brief",
        Description = "Brief for the review",
        CreatedByUserId = "creator",
        CreatedByRole = RoleNames.Comdt,
        AssignedToUserId = "assignee",
        AssignedToRole = RoleNames.Ta,
        DueDate = TestClock.FixedUtc.Date.AddDays(7),
        Priority = "Normal",
        Status = ActionTaskStatuses.Assigned,
    };

    private static IFormFile TextFile()
    {
        var content = new MemoryStream("progress evidence"u8.ToArray());
        return new FormFile(content, 0, content.Length, "files", "evidence.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain",
        };
    }

    private static NotificationDispatch Dispatch(NotificationKind kind) => new()
    {
        RecipientUserId = "assignee",
        Kind = kind,
        PayloadJson = "{}",
        CreatedUtc = DateTime.UtcNow,
    };

    /// <summary>Queues an outbox row into the caller's context like the real producer, or throws.</summary>
    private sealed class QueueingNotifier(ApplicationDbContext db, bool fail) : NotifierBase
    {
        public int Attempts { get; private set; }

        protected override Task NotifyAsync(NotificationKind kind, CancellationToken cancellationToken)
        {
            Attempts++;
            if (fail)
            {
                throw new InvalidOperationException("Injected notification failure.");
            }

            db.NotificationDispatches.Add(Dispatch(kind));
            return Task.CompletedTask;
        }
    }

    /// <summary>Cancels the request mid-notification, as an aborted HTTP request would.</summary>
    private sealed class CancellingNotifier(CancellationTokenSource cancellation) : NotifierBase
    {
        protected override Task NotifyAsync(NotificationKind kind, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private abstract class NotifierBase : IActionTaskNotificationService
    {
        protected abstract Task NotifyAsync(NotificationKind kind, CancellationToken cancellationToken);

        public Task NotifyTaskAssignedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskAssigned, cancellationToken);

        public Task NotifyTaskReassignedAsync(ActionTaskItem task, string? previousAssigneeUserId, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskAssigned, cancellationToken);

        public Task NotifyTaskDetailsUpdatedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskProgressUpdated, cancellationToken);

        public Task NotifyProgressUpdatedAsync(ActionTaskItem task, ActionTaskUpdate? update, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskProgressUpdated, cancellationToken);

        public Task NotifyConferenceDirectionEditedAsync(ActionTaskItem task, ActionTaskUpdate update, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskProgressUpdated, cancellationToken);

        public Task NotifyConferenceDirectionDeletedAsync(ActionTaskItem task, ActionTaskUpdate update, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskProgressUpdated, cancellationToken);

        public Task NotifyStatusChangedAsync(ActionTaskItem task, string previousStatus, string newStatus, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskStatusChanged, cancellationToken);

        public Task NotifyTaskBlockedAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskBlocked, cancellationToken);

        public Task NotifySubmittedForClosureAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskSubmittedForClosure, cancellationToken);

        public Task NotifyTaskClosedAsync(ActionTaskItem task, string actorUserId, bool closedByCommandAuthority, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskClosed, cancellationToken);

        public Task NotifyDueDateChangedAsync(ActionTaskItem task, DateTime oldDate, DateTime newDate, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskDueDateChanged, cancellationToken);

        public Task NotifyMovedToBacklogAsync(ActionTaskItem task, string? previousAssigneeUserId, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskMovedToBacklog, cancellationToken);

        public Task NotifyRemovedFromSprintAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskRemovedFromSprint, cancellationToken);

        public Task NotifyAddedToSprintAsync(ActionTaskItem task, string actorUserId, CancellationToken cancellationToken = default)
            => NotifyAsync(NotificationKind.ActionTaskAddedToSprint, cancellationToken);
    }

    private sealed class TestClock : IActionTrackerClock
    {
        public static readonly DateTime FixedUtc = new(2030, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow => FixedUtc;
        public DateTime UtcToday => FixedUtc.Date;
        public DateTime IstNow => FixedUtc.AddHours(5.5);
        public DateTime IstToday => IstNow.Date;
    }

    private sealed class TestUploadRootProvider(string root) : IUploadRootProvider
    {
        public string RootPath => root;
        public string GetProjectRoot(int projectId) => root;
        public string GetProjectPhotosRoot(int projectId) => root;
        public string GetProjectDocumentsRoot(int projectId) => root;
        public string GetProjectCommentsRoot(int projectId) => root;
        public string GetProjectVideosRoot(int projectId) => root;
        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => root;
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
