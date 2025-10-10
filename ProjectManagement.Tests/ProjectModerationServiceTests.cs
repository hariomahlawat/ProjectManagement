using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectModerationServiceTests
{
    [Fact]
    public async Task MoveToTrashAsync_RequiresReason()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Alpha",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 0, 0, 0, TimeSpan.Zero));
        using var temp = new TempDirectory();
        var service = CreateService(db, clock, temp);

        var result = await service.MoveToTrashAsync(1, "actor", "   ");

        Assert.Equal(ProjectModerationStatus.ValidationFailed, result.Status);
        Assert.Equal("A reason is required to move a project to Trash.", result.Error);
    }

    [Fact]
    public async Task MoveToTrashAsync_SetsDeletionMetadata()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Projects.Add(new Project
        {
            Id = 2,
            Name = "Bravo",
            CreatedAt = new DateTime(2024, 1, 2),
            CreatedByUserId = "creator"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 2, 12, 0, 0, TimeSpan.Zero));
        using var temp = new TempDirectory();
        var service = CreateService(db, clock, temp);

        var result = await service.MoveToTrashAsync(2, "moderator", " Backfill cleanup ");

        Assert.Equal(ProjectModerationStatus.Success, result.Status);

        var project = await db.Projects.SingleAsync(p => p.Id == 2);
        Assert.True(project.IsDeleted);
        Assert.Equal(clock.UtcNow, project.DeletedAt);
        Assert.Equal("moderator", project.DeletedByUserId);
        Assert.Equal("Backfill cleanup", project.DeleteReason);
        Assert.Equal("Trash", project.DeleteMethod);

        var audit = await db.ProjectAudits.SingleAsync(a => a.ProjectId == 2);
        Assert.Equal("Trash", audit.Action);
        Assert.Equal("moderator", audit.PerformedByUserId);
        Assert.Equal("Backfill cleanup", audit.Reason);
        Assert.Equal(clock.UtcNow, audit.PerformedAt);
    }

    [Fact]
    public async Task RestoreFromTrashAsync_ClearsDeletionMetadata()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Projects.Add(new Project
        {
            Id = 3,
            Name = "Charlie",
            CreatedAt = new DateTime(2024, 1, 3),
            CreatedByUserId = "creator",
            IsDeleted = true,
            DeletedAt = new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero),
            DeletedByUserId = "moderator",
            DeleteReason = "Cleanup",
            DeleteMethod = "Trash",
            DeleteApprovedByUserId = "admin"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 3, 8, 30, 0, TimeSpan.Zero));
        using var temp = new TempDirectory();
        var service = CreateService(db, clock, temp);

        var result = await service.RestoreFromTrashAsync(3, "admin");

        Assert.Equal(ProjectModerationStatus.Success, result.Status);

        var project = await db.Projects.SingleAsync(p => p.Id == 3);
        Assert.False(project.IsDeleted);
        Assert.Null(project.DeletedAt);
        Assert.Null(project.DeletedByUserId);
        Assert.Null(project.DeleteReason);
        Assert.Null(project.DeleteMethod);
        Assert.Null(project.DeleteApprovedByUserId);

        var audit = await db.ProjectAudits.SingleAsync(a => a.ProjectId == 3);
        Assert.Equal("RestoreTrash", audit.Action);
        Assert.Equal("admin", audit.PerformedByUserId);
        Assert.Equal(clock.UtcNow, audit.PerformedAt);
        Assert.Null(audit.Reason);
    }

    [Fact]
    public async Task PurgeExpiredAsync_RemovesExpiredProjects()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Projects.AddRange(
            new Project
            {
                Id = 10,
                Name = "Old",
                CreatedAt = new DateTime(2024, 1, 10),
                CreatedByUserId = "creator",
                IsDeleted = true,
                DeletedAt = new DateTimeOffset(2024, 8, 1, 0, 0, 0, TimeSpan.Zero),
                DeleteReason = "Cleanup"
            },
            new Project
            {
                Id = 11,
                Name = "Recent",
                CreatedAt = new DateTime(2024, 1, 11),
                CreatedByUserId = "creator",
                IsDeleted = true,
                DeletedAt = new DateTimeOffset(2024, 9, 25, 0, 0, 0, TimeSpan.Zero),
                DeleteReason = "Later"
            });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 15, 0, 0, 0, TimeSpan.Zero));
        using var temp = new TempDirectory();
        var service = CreateService(db, clock, temp);

        var cutoff = clock.UtcNow.AddDays(-30);
        var purged = await service.PurgeExpiredAsync(cutoff, includeAssets: false);

        Assert.Equal(1, purged);
        Assert.Null(await db.Projects.FindAsync(10));
        Assert.NotNull(await db.Projects.FindAsync(11));

        var audit = await db.ProjectAudits.SingleAsync(a => a.ProjectId == 10);
        Assert.Equal("Purge", audit.Action);
        Assert.Equal("system", audit.PerformedByUserId);
    }

    private static ProjectModerationService CreateService(ApplicationDbContext db, FakeClock clock, TempDirectory temp)
    {
        return new ProjectModerationService(
            db,
            clock,
            NullLogger<ProjectModerationService>.Instance,
            new TestUploadRootProvider(temp.Path),
            Options.Create(new ProjectDocumentOptions()));
    }

    private static async Task<ApplicationDbContext> CreateContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed class TestUploadRootProvider : IUploadRootProvider
    {
        private readonly string _root;

        public TestUploadRootProvider(string root) => _root = root;

        public string RootPath => _root;

        public string GetProjectRoot(int projectId) => Path.Combine(_root, projectId.ToString());

        public string GetProjectPhotosRoot(int projectId) => Path.Combine(GetProjectRoot(projectId), "photos");

        public string GetProjectDocumentsRoot(int projectId) => Path.Combine(GetProjectRoot(projectId), "docs");

        public string GetProjectCommentsRoot(int projectId) => Path.Combine(GetProjectRoot(projectId), "comments");

        public string GetProjectVideosRoot(int projectId) => Path.Combine(GetProjectRoot(projectId), "videos");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pm-moderation-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }
}
