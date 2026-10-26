using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
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

        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "Projects.Purge");
        Assert.Equal("system", audit.UserId);
    }


    [Fact]
    public async Task PurgeAsync_QuarantinesAndDeletesProjectAssetsAfterDatabaseCommit()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Projects.Add(new Project
        {
            Id = 12,
            Name = "With assets",
            CreatedAt = new DateTime(2024, 1, 12),
            CreatedByUserId = "creator",
            IsDeleted = true,
            DeletedAt = new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero),
            DeleteReason = "Cleanup"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 15, 0, 0, 0, TimeSpan.Zero));
        using var temp = new TempDirectory();
        var projectRoot = Path.Combine(temp.Path, "projects", "12");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "asset.txt"), "asset");
        var service = CreateService(db, clock, temp);

        var result = await service.PurgeAsync(12, "admin", includeAssets: true);

        Assert.Equal(ProjectModerationStatus.Success, result.Status);
        Assert.Null(await db.Projects.FindAsync(12));
        Assert.False(Directory.Exists(projectRoot));
        var quarantineRoot = Path.Combine(temp.Path, ".purge-quarantine");
        Assert.False(Directory.Exists(quarantineRoot) && Directory.EnumerateFileSystemEntries(quarantineRoot, "*", SearchOption.AllDirectories).Any());
    }


    [Fact]
    public async Task PurgeAsync_RemovesActualCorrectionRevisionsAndItems()
    {
        // Phase D-2: items restrict their revision and revisions link to each other, so
        // purge must clear the links and delete items before revisions and the project.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Projects.Add(new Project
        {
            Id = 14,
            Name = "With actual proposals",
            CreatedAt = new DateTime(2024, 1, 12),
            CreatedByUserId = "creator",
            IsDeleted = true,
            DeletedAt = new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero),
            DeleteReason = "Cleanup"
        });
        db.Projects.Add(new Project { Id = 15, Name = "Kept", CreatedAt = new DateTime(2024, 1, 12), CreatedByUserId = "creator" });
        await db.SaveChangesAsync();
        await ProjectManagement.Tests.Stages.ActualCorrectionSchemaTests.SeedSupersededPairAsync(db, 14);
        db.ActualCorrectionRevisions.Add(ProjectManagement.Tests.Stages.ActualCorrectionSchemaTests.Revision(15, 1));
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 15, 0, 0, 0, TimeSpan.Zero));
        using var temp = new TempDirectory();
        var result = await CreateService(db, clock, temp).PurgeAsync(14, "admin", includeAssets: false);

        Assert.Equal(ProjectModerationStatus.Success, result.Status);
        Assert.False(await db.Projects.AnyAsync(project => project.Id == 14));
        Assert.False(await db.ActualCorrectionRevisions.AnyAsync(revision => revision.ProjectId == 14));
        Assert.Equal(0, await db.ActualCorrectionItems.CountAsync());
        Assert.Equal(1, await db.ActualCorrectionRevisions.CountAsync(revision => revision.ProjectId == 15));
    }

    [Fact]
    public async Task PurgeAsync_RemovesProliferationRecordsAmendmentsAndCountingRules()
    {
        // Proliferation tables have no foreign key to Projects, so purge removes them itself.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Projects.Add(new Project
        {
            Id = 16,
            Name = "Purged simulator",
            CreatedAt = new DateTime(2024, 1, 12),
            CreatedByUserId = "creator",
            IsDeleted = true,
            DeletedAt = new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero),
            DeleteReason = "Cleanup"
        });
        db.Projects.Add(new Project { Id = 17, Name = "Kept", CreatedAt = new DateTime(2024, 1, 12), CreatedByUserId = "creator" });
        await db.SaveChangesAsync();

        var approved = ProliferationYearly(16, ApprovalStatus.Approved, amendsId: null);
        db.ProliferationYearlies.Add(approved);
        await db.SaveChangesAsync();
        db.ProliferationYearlies.AddRange(
            ProliferationYearly(16, ApprovalStatus.Pending, amendsId: approved.Id),
            ProliferationYearly(17, ApprovalStatus.Approved, amendsId: null));
        db.ProliferationGranularEntries.Add(new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = 16,
            Source = ProliferationSource.Sdd,
            UnitName = "Unit",
            ProliferationDate = new DateOnly(2024, 5, 1),
            Quantity = 3,
            ApprovalStatus = ApprovalStatus.Approved,
            SubmittedByUserId = "creator",
            RowVersion = Guid.NewGuid().ToByteArray()
        });
        db.ProliferationYearPreferences.Add(new ProliferationYearPreference
        {
            Id = Guid.NewGuid(),
            ProjectId = 16,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            Mode = YearPreferenceMode.UseYearly,
            SetByUserId = "creator"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 15, 0, 0, 0, TimeSpan.Zero));
        using var temp = new TempDirectory();
        var result = await CreateService(db, clock, temp).PurgeAsync(16, "admin", includeAssets: false);

        Assert.Equal(ProjectModerationStatus.Success, result.Status);
        Assert.False(await db.ProliferationYearlies.AnyAsync(record => record.ProjectId == 16));
        Assert.False(await db.ProliferationGranularEntries.AnyAsync(entry => entry.ProjectId == 16));
        Assert.False(await db.ProliferationYearPreferences.AnyAsync(rule => rule.ProjectId == 16));
        Assert.Equal(1, await db.ProliferationYearlies.CountAsync(record => record.ProjectId == 17));
    }

    private static ProliferationYearly ProliferationYearly(int projectId, ApprovalStatus status, Guid? amendsId)
        => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Source = ProliferationSource.Sdd,
            Year = 2024,
            TotalQuantity = 5,
            ApprovalStatus = status,
            AmendsId = amendsId,
            SubmittedByUserId = "creator",
            RowVersion = Guid.NewGuid().ToByteArray()
        };

    [Fact]
    public async Task PurgeAsync_RestoresAssetsWhenDatabaseTransactionFails()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateContextAsync(connection);
        db.Projects.Add(new Project
        {
            Id = 13,
            Name = "Rollback assets",
            CreatedAt = new DateTime(2024, 1, 13),
            CreatedByUserId = "creator",
            IsDeleted = true,
            DeletedAt = new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero),
            DeleteReason = "Cleanup"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 15, 0, 0, 0, TimeSpan.Zero));
        using var temp = new TempDirectory();
        var projectRoot = Path.Combine(temp.Path, "projects", "13");
        Directory.CreateDirectory(projectRoot);
        var assetPath = Path.Combine(projectRoot, "asset.txt");
        await File.WriteAllTextAsync(assetPath, "asset");
        var service = CreateService(db, clock, temp, new ThrowingAuditService());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PurgeAsync(13, "admin", includeAssets: true));

        Assert.NotNull(await db.Projects.AsNoTracking().SingleOrDefaultAsync(project => project.Id == 13));
        Assert.True(File.Exists(assetPath));
    }

    private static ProjectModerationService CreateService(
        ApplicationDbContext db,
        FakeClock clock,
        TempDirectory temp,
        IAuditService? audit = null)
    {
        return new ProjectModerationService(
            db,
            clock,
            NullLogger<ProjectModerationService>.Instance,
            new TestUploadRootProvider(temp.Path),
            Options.Create(new ProjectDocumentOptions()),
            audit ?? new AuditService(db, new HttpContextAccessor()));
    }

    private sealed class ThrowingAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null) =>
            throw new InvalidOperationException("Simulated audit failure.");
    }

    private static async Task<ApplicationDbContext> CreateContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Users.AddRange(new[] { "creator", "actor", "moderator", "admin" }
            .Select(id => new ApplicationUser { Id = id, UserName = id }));
        await context.SaveChangesAsync();
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

        public string GetSocialMediaRoot(string storagePrefix, Guid eventId)
        {
            if (eventId == Guid.Empty)
            {
                throw new ArgumentException("Event identifier cannot be empty.", nameof(eventId));
            }

            if (string.IsNullOrWhiteSpace(storagePrefix))
            {
                storagePrefix = "org/social/{eventId}";
            }

            const string token = "{eventId}";
            var trimmed = storagePrefix.Trim();
            if (!trimmed.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Storage prefix must contain the token '{token}'.", nameof(storagePrefix));
            }

            var sanitizedPrefix = trimmed
                .TrimStart('/', '\\')
                .TrimEnd('/', '\\')
                .Replace(token, eventId.ToString("D"), StringComparison.OrdinalIgnoreCase);

            var relativePath = sanitizedPrefix
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            return Path.Combine(_root, relativePath);
        }
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
