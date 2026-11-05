using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectKeyPointServiceTests
{
    private const string RowVersion = "AQ==";

    [Fact]
    public void Catalog_HasTheFiveAgreedHeadingsInOrder()
    {
        Assert.Equal(
            new[] { "Training gap", "Op necessity", "Training realism", "Ease of learning", "Proliferation potential" },
            ProjectKeyPointCatalog.All.Select(definition => definition.Heading));
        Assert.Equal(
            Enum.GetValues<ProjectKeyPointKind>(),
            ProjectKeyPointCatalog.All.Select(definition => definition.Kind));
        Assert.All(ProjectKeyPointCatalog.All, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Hint)));
    }

    [Fact]
    public async Task SaveKeyPoints_AddsUpdatesAndRemovesByHeading()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db);
        db.ProjectKeyPoints.AddRange(
            new ProjectKeyPoint { ProjectId = 1, Kind = ProjectKeyPointKind.TrainingGap, Text = "Old gap" },
            new ProjectKeyPoint { ProjectId = 1, Kind = ProjectKeyPointKind.OpNecessity, Text = "To be cleared" });
        await db.SaveChangesAsync();
        var audit = new RecordingAuditQueue();

        var result = await CreateService(db, audit).SaveKeyPointsAsync(
            1,
            new[] { "  New gap ", " ", "Replicates the\r\nsight   picture", null, null },
            RowVersion,
            "user-1",
            "User One");

        Assert.True(result.Succeeded, result.Error);
        var points = await db.ProjectKeyPoints.AsNoTracking()
            .OrderBy(point => point.Kind)
            .ToListAsync();
        Assert.Equal(
            new[]
            {
                (ProjectKeyPointKind.TrainingGap, "New gap"),
                (ProjectKeyPointKind.TrainingRealism, "Replicates the sight picture")
            },
            points.Select(point => (point.Kind, point.Text)));

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("Project.Content.KeyPointsUpdated", entry.Action);
        Assert.Equal("2", entry.Data["RecordedCount"]);
        Assert.Equal("user-1", (await db.Projects.AsNoTracking().SingleAsync()).ContentUpdatedByUserId);
    }

    [Fact]
    public async Task SaveKeyPoints_AcceptsTheLimitAndRejectsOneCharacterMore()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db);
        var service = CreateService(db, new RecordingAuditQueue());
        var atLimit = new string('a', ProjectFieldLimits.KeyPointMaxLength);

        var accepted = await service.SaveKeyPointsAsync(1, new[] { atLimit }, RowVersion, "user-1", "User One");
        Assert.True(accepted.Succeeded, accepted.Error);

        var rejected = await service.SaveKeyPointsAsync(
            1,
            new[] { null, atLimit + "b" },
            Convert.ToBase64String((await db.Projects.AsNoTracking().SingleAsync()).RowVersion),
            "user-1",
            "User One");

        Assert.False(rejected.Succeeded);
        Assert.Contains("Op necessity", rejected.Error);
        Assert.Contains("130", rejected.Error);
        var stored = Assert.Single(await db.ProjectKeyPoints.AsNoTracking().ToListAsync());
        Assert.Equal(ProjectKeyPointKind.TrainingGap, stored.Kind);
    }

    [Fact]
    public async Task SaveKeyPoints_RejectsMoreValuesThanHeadings()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db);

        var result = await CreateService(db, new RecordingAuditQueue()).SaveKeyPointsAsync(
            1,
            new[] { "a", "b", "c", "d", "e", "f" },
            RowVersion,
            "user-1",
            "User One");

        Assert.False(result.Succeeded);
        Assert.Empty(await db.ProjectKeyPoints.ToListAsync());
    }

    [Fact]
    public async Task SaveKeyPoints_ReportsAMissingProject()
    {
        await using var db = CreateContext();

        var result = await CreateService(db, new RecordingAuditQueue()).SaveKeyPointsAsync(
            404,
            new[] { "a" },
            RowVersion,
            "user-1",
            "User One");

        Assert.True(result.NotFound);
    }

    private static ProjectContentService CreateService(ApplicationDbContext db, IProjectContentAuditQueue audit)
        => new(db, new FixedClock(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero)), audit, NullLogger<ProjectContentService>.Instance);

    private static ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task SeedProjectAsync(ApplicationDbContext db)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "ATGM crew procedural trainer",
            CreatedByUserId = "creator",
            CreatedAt = new DateTime(2026, 1, 1),
            RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();
    }

    private sealed class RecordingAuditQueue : IProjectContentAuditQueue
    {
        public List<ProjectContentAuditEntry> Entries { get; } = new();

        public bool TryEnqueue(ProjectContentAuditEntry entry)
        {
            Entries.Add(entry);
            return true;
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
