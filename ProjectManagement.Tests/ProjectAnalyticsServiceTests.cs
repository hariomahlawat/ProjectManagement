using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectAnalyticsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly TestClock _clock;
    private readonly ProjectAnalyticsService _service;

    public ProjectAnalyticsServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        _clock = new TestClock(new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero));
        _service = new ProjectAnalyticsService(_db, _clock);
    }

    [Fact]
    public async Task CategoryShare_ReturnsCountsPerCategory()
    {
        var catA = new ProjectCategory { Name = "A" };
        var catB = new ProjectCategory { Name = "B" };

        _db.ProjectCategories.AddRange(catA, catB);
        await _db.SaveChangesAsync();

        _db.Projects.AddRange(
            new Project { Name = "One", CategoryId = catA.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "u", CreatedAt = DateTime.UtcNow },
            new Project { Name = "Two", CategoryId = catA.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "u", CreatedAt = DateTime.UtcNow },
            new Project { Name = "Three", CategoryId = catB.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "u", CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetCategoryShareAsync(ProjectLifecycleFilter.Active);

        Assert.Equal(3, result.Total);
        var sliceA = Assert.Single(result.Slices.Where(s => s.CategoryId == catA.Id));
        Assert.Equal(2, sliceA.Count);
        var sliceB = Assert.Single(result.Slices.Where(s => s.CategoryId == catB.Id));
        Assert.Equal(1, sliceB.Count);
    }

    [Fact]
    public async Task TopOverdueProjects_ReturnsOrderedBySlip()
    {
        var category = new ProjectCategory { Name = "Infra" };
        _db.ProjectCategories.Add(category);
        await _db.SaveChangesAsync();

        var today = new DateOnly(2024, 6, 15);

        _db.Projects.AddRange(
            new Project
            {
                Name = "Late A",
                CategoryId = category.Id,
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedByUserId = "u",
                CreatedAt = DateTime.UtcNow,
                ProjectStages =
                {
                    new ProjectStage
                    {
                        StageCode = "FS",
                        SortOrder = 10,
                        Status = StageStatus.InProgress,
                        PlannedDue = today.AddDays(-10)
                    }
                }
            },
            new Project
            {
                Name = "Late B",
                CategoryId = category.Id,
                LifecycleStatus = ProjectLifecycleStatus.Active,
                CreatedByUserId = "u",
                CreatedAt = DateTime.UtcNow,
                ProjectStages =
                {
                    new ProjectStage
                    {
                        StageCode = "FS",
                        SortOrder = 10,
                        Status = StageStatus.InProgress,
                        PlannedDue = today.AddDays(-5)
                    }
                }
            }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetTopOverdueProjectsAsync(ProjectLifecycleFilter.Active, null, 5);

        Assert.Equal(2, result.Projects.Count);
        Assert.Equal("Late A", result.Projects[0].Name);
        Assert.Equal(10, result.Projects[0].SlipDays);
        Assert.Equal("Late B", result.Projects[1].Name);
        Assert.Equal(5, result.Projects[1].SlipDays);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan span) => UtcNow = UtcNow.Add(span);
    }
}
