using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Security;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskCollaborationServiceTests
{
    [Fact]
    public async Task AttachmentValidation_RejectsTypeCountAndSize()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner");

        var badType = BuildFile("bad.exe", "application/octet-stream", 128);
        var overSize = BuildFile("big.txt", "text/plain", 26L * 1024 * 1024);
        var manyFiles = Enumerable.Range(1, 11).Select(i => BuildFile($"f{i}.txt", "text/plain", 10)).ToArray();

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAsync(task.Id, "body", ActionTaskUpdateTypes.Comment, "owner", RoleNames.Ta, [badType]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAsync(task.Id, "body", ActionTaskUpdateTypes.Comment, "owner", RoleNames.Ta, [overSize]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAsync(task.Id, "body", ActionTaskUpdateTypes.Comment, "owner", RoleNames.Ta, manyFiles));
    }

    [Fact]
    public async Task InaccessibleReadAndWrite_AreDenied()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner");

        // SECTION: Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetUpdatesAsync(task.Id, "other", RoleNames.Ta));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAsync(task.Id, "body", ActionTaskUpdateTypes.Progress, "other", RoleNames.Ta, Array.Empty<IFormFile>()));
    }


    [Fact]
    public async Task AddUpdateAsync_UsesClockUtcNowForUpdateTimestamp()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner");

        // SECTION: Act
        var update = await service.AddUpdateAsync(task.Id, "body", ActionTaskUpdateTypes.Progress, "owner", RoleNames.Ta, Array.Empty<IFormFile>());

        // SECTION: Assert
        Assert.Equal(TestActionTrackerClock.FixedUtcNow, update.CreatedAtUtc);
    }


    [Fact]
    public async Task AddUpdateAndMaybeChangeStatusAsync_RejectsMoreThanMaximumAttachmentsWithoutPartialSave()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner");
        var manyFiles = Enumerable.Range(1, 11).Select(i => BuildFile($"f{i}.txt", "text/plain", 10)).ToArray();

        // SECTION: Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAndMaybeChangeStatusAsync(task.Id, "moving forward", ActionTaskStatuses.InProgress, "owner", RoleNames.Ta, manyFiles, task.RowVersion));

        // SECTION: Assert
        Assert.Equal("A maximum of 10 files can be attached per update.", ex.Message);
        var unchanged = await db.ActionTasks.SingleAsync(x => x.Id == task.Id);
        Assert.Equal(ActionTaskStatuses.Assigned, unchanged.Status);
        Assert.Empty(await db.ActionTaskUpdates.Where(x => x.TaskId == task.Id).ToListAsync());
        Assert.Empty(await db.ActionTaskAttachments.Where(x => x.TaskId == task.Id).ToListAsync());
        Assert.Empty(await db.ActionTaskAuditLogs.Where(x => x.TaskId == task.Id).ToListAsync());
    }

    [Fact]
    public async Task AddUpdateAndMaybeChangeStatusAsync_AllowsMaximumPermittedAttachments()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner");
        var maxFiles = Enumerable.Range(1, 10).Select(i => BuildFile($"f{i}.txt", "text/plain", 10)).ToArray();

        // SECTION: Act
        var update = await service.AddUpdateAndMaybeChangeStatusAsync(task.Id, "moving forward", ActionTaskStatuses.InProgress, "owner", RoleNames.Ta, maxFiles, task.RowVersion);

        // SECTION: Assert
        Assert.NotNull(update);
        Assert.Equal(ActionTaskUpdateTypes.Progress, update.UpdateType);
        Assert.Equal("moving forward", update.Body);
        var changed = await db.ActionTasks.SingleAsync(x => x.Id == task.Id);
        Assert.Equal(ActionTaskStatuses.InProgress, changed.Status);
        Assert.Equal(1, await db.ActionTaskUpdates.CountAsync(x => x.TaskId == task.Id));
        Assert.Equal(10, await db.ActionTaskAttachments.CountAsync(x => x.TaskId == task.Id));
    }

    [Fact]
    public async Task AddUpdateAndMaybeChangeStatusAsync_PreservesFileTypeAndSizeValidation()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var badTypeTask = await SeedTaskAsync(db, "owner");
        var overSizeTask = await SeedTaskAsync(db, "owner");
        var badType = BuildFile("bad.exe", "application/octet-stream", 128);
        var overSize = BuildFile("big.txt", "text/plain", 26L * 1024 * 1024);

        // SECTION: Act + Assert
        var badTypeEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAndMaybeChangeStatusAsync(badTypeTask.Id, "body", null, "owner", RoleNames.Ta, [badType], badTypeTask.RowVersion));
        Assert.Equal("This file type is not allowed.", badTypeEx.Message);

        var overSizeEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAndMaybeChangeStatusAsync(overSizeTask.Id, "body", null, "owner", RoleNames.Ta, [overSize], overSizeTask.RowVersion));
        Assert.Equal("File size exceeds the 25 MB limit.", overSizeEx.Message);

        Assert.Empty(await db.ActionTaskUpdates.ToListAsync());
        Assert.Empty(await db.ActionTaskAttachments.ToListAsync());
    }


    [Fact]
    public async Task AddUpdateAndMaybeChangeStatusAsync_StatusOnlyAssignedToInProgressCreatesProgressAndAuditEntries()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner", ActionTaskStatuses.Assigned);

        // SECTION: Act
        var update = await service.AddUpdateAndMaybeChangeStatusAsync(task.Id, string.Empty, ActionTaskStatuses.InProgress, "owner", RoleNames.Ta, Array.Empty<IFormFile>(), task.RowVersion);

        // SECTION: Assert
        Assert.NotNull(update);
        Assert.Equal(ActionTaskUpdateTypes.Progress, update.UpdateType);
        Assert.Equal("Task marked as in progress.", update.Body);

        var changed = await db.ActionTasks.SingleAsync(x => x.Id == task.Id);
        Assert.Equal(ActionTaskStatuses.InProgress, changed.Status);

        var audit = await db.ActionTaskAuditLogs.SingleAsync(x => x.TaskId == task.Id && x.ActionType == "StatusUpdated");
        Assert.Equal(ActionTaskStatuses.Assigned, audit.OldValue);
        Assert.Equal(ActionTaskStatuses.InProgress, audit.NewValue);
    }

    [Fact]
    public async Task AddUpdateAndMaybeChangeStatusAsync_StatusOnlyBlockedToInProgressCreatesAutomaticProgressEntry()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner", ActionTaskStatuses.Blocked);

        // SECTION: Act
        var update = await service.AddUpdateAndMaybeChangeStatusAsync(task.Id, "", ActionTaskStatuses.InProgress, "owner", RoleNames.Ta, Array.Empty<IFormFile>(), task.RowVersion);

        // SECTION: Assert
        Assert.NotNull(update);
        Assert.Equal("Task marked as in progress.", update.Body);
        Assert.Equal(1, await db.ActionTaskUpdates.CountAsync(x => x.TaskId == task.Id && x.UpdateType == ActionTaskUpdateTypes.Progress));
        Assert.Equal(1, await db.ActionTaskAuditLogs.CountAsync(x => x.TaskId == task.Id && x.ActionType == "StatusUpdated"));
    }

    [Fact]
    public async Task AddUpdateAndMaybeChangeStatusAsync_EmptyNoOpCreatesNoProgressEntryAndReturnsClearMessage()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner", ActionTaskStatuses.Assigned);

        // SECTION: Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAndMaybeChangeStatusAsync(task.Id, " ", null, "owner", RoleNames.Ta, Array.Empty<IFormFile>(), task.RowVersion));

        // SECTION: Assert
        Assert.Equal("No update was applied.", ex.Message);
        Assert.Empty(await db.ActionTaskUpdates.Where(x => x.TaskId == task.Id).ToListAsync());
        Assert.Empty(await db.ActionTaskAuditLogs.Where(x => x.TaskId == task.Id).ToListAsync());
    }

    [Theory]
    [InlineData(ActionTaskStatuses.Blocked, "Remarks are required when marking a task as blocked.")]
    [InlineData(ActionTaskStatuses.Submitted, "Remarks are required when submitting a task.")]
    public async Task AddUpdateAndMaybeChangeStatusAsync_ImportantTransitionsStillRequireUserNote(string status, string expectedMessage)
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner", ActionTaskStatuses.Assigned);

        // SECTION: Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddUpdateAndMaybeChangeStatusAsync(task.Id, "", status, "owner", RoleNames.Ta, Array.Empty<IFormFile>(), task.RowVersion));

        // SECTION: Assert
        Assert.Equal(expectedMessage, ex.Message);
        Assert.Empty(await db.ActionTaskUpdates.Where(x => x.TaskId == task.Id).ToListAsync());
    }

    [Fact]
    public async Task AddUpdateAndMaybeChangeStatusAsync_FileOnlyUpdateCreatesSupportingFileProgressEntry()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner", ActionTaskStatuses.Assigned);
        var file = BuildFile("support.txt", "text/plain", 10);

        // SECTION: Act
        var update = await service.AddUpdateAndMaybeChangeStatusAsync(task.Id, " ", null, "owner", RoleNames.Ta, [file], task.RowVersion);

        // SECTION: Assert
        Assert.NotNull(update);
        Assert.Equal("Supporting file uploaded.", update.Body);
        Assert.Equal(1, await db.ActionTaskAttachments.CountAsync(x => x.TaskId == task.Id && x.UpdateId == update.Id));
    }

    [Fact]
    public async Task AddUpdateAndMaybeChangeStatusAsync_UserNoteAndStatusChangeUsesUserNote()
    {
        // SECTION: Arrange
        await using var db = CreateDb();
        var service = CreateService(db);
        var task = await SeedTaskAsync(db, "owner", ActionTaskStatuses.Assigned);

        // SECTION: Act
        var update = await service.AddUpdateAndMaybeChangeStatusAsync(task.Id, "  Started the work.  ", ActionTaskStatuses.InProgress, "owner", RoleNames.Ta, Array.Empty<IFormFile>(), task.RowVersion);

        // SECTION: Assert
        Assert.NotNull(update);
        Assert.Equal("Started the work.", update.Body);
        Assert.DoesNotContain("Task marked as in progress.", await db.ActionTaskUpdates.Where(x => x.TaskId == task.Id).Select(x => x.Body).ToListAsync());
    }

    private static ActionTaskCollaborationService CreateService(ApplicationDbContext db)
        => new(db, new ActionTaskPermissionService(), new TestUploadRootProvider(), new PassFileSecurityValidator(), new StubUrlBuilder(), new TestActionTrackerClock());

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<ActionTaskItem> SeedTaskAsync(ApplicationDbContext db, string assignee, string status = ActionTaskStatuses.Assigned)
    {
        var task = new ActionTaskItem
        {
            Title = "Task",
            Description = "Desc",
            CreatedByUserId = "creator",
            AssignedToUserId = assignee,
            CreatedByRole = RoleNames.HoD,
            AssignedToRole = RoleNames.Ta,
            DueDate = DateTime.UtcNow.AddDays(1),
            Priority = "Normal",
            AssignedOn = DateTime.UtcNow,
            Status = status
        };
        db.ActionTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static IFormFile BuildFile(string name, string contentType, long bytes)
    {
        var stream = new MemoryStream(new byte[bytes]);
        return new FormFile(stream, 0, bytes, "files", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }


    private sealed class TestActionTrackerClock : IActionTrackerClock
    {
        public static readonly DateTime FixedUtcNow = new(2030, 1, 15, 6, 30, 0);

        public DateTime UtcNow => FixedUtcNow;
        public DateTime UtcToday => UtcNow.Date;
        public DateTime IstNow => FixedUtcNow.AddHours(5.5);
        public DateTime IstToday => IstNow.Date;
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
