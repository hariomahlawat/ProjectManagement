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

    private static ActionTaskCollaborationService CreateService(ApplicationDbContext db)
        => new(db, new ActionTaskPermissionService(), new TestUploadRootProvider(), new PassFileSecurityValidator(), new StubUrlBuilder(), new TestActionTrackerClock());

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<ActionTaskItem> SeedTaskAsync(ApplicationDbContext db, string assignee)
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
            Status = ActionTaskStatuses.Assigned
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
