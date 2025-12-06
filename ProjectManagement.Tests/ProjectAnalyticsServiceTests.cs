using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
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
    private readonly ProjectCategoryHierarchyService _categories;
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
        _categories = new ProjectCategoryHierarchyService(_db);
        _service = new ProjectAnalyticsService(_db, _clock, _categories, new WorkflowStageMetadataProvider());
    }

    [Fact]
    public async Task CategoryShare_ReturnsCountsPerCategory()
    {
        var catA = new ProjectCategory { Name = "A" };
        var catB = new ProjectCategory { Name = "B" };
        var techA = new TechnicalCategory { Name = "Digital" };
        var techB = new TechnicalCategory { Name = "Mechanical" };

        _db.ProjectCategories.AddRange(catA, catB);
        await _db.TechnicalCategories.AddRangeAsync(techA, techB);
        await _db.SaveChangesAsync();

        _db.Projects.AddRange(
            new Project { Name = "One", CategoryId = catA.Id, TechnicalCategoryId = techA.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "u", CreatedAt = DateTime.UtcNow },
            new Project { Name = "Two", CategoryId = catA.Id, TechnicalCategoryId = techB.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "u", CreatedAt = DateTime.UtcNow },
            new Project { Name = "Three", CategoryId = catB.Id, TechnicalCategoryId = techA.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "u", CreatedAt = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetCategoryShareAsync(ProjectLifecycleFilter.Active);

        Assert.Equal(3, result.Total);
        var sliceA = Assert.Single(result.Slices.Where(s => s.CategoryId == catA.Id));
        Assert.Equal(2, sliceA.Count);
        var sliceB = Assert.Single(result.Slices.Where(s => s.CategoryId == catB.Id));
        Assert.Equal(1, sliceB.Count);

        var filtered = await _service.GetCategoryShareAsync(ProjectLifecycleFilter.Active, null, techA.Id);
        Assert.Equal(2, filtered.Total);
        Assert.All(filtered.Slices, s =>
        {
            Assert.Equal(techA.Id, s.TechnicalCategoryId);
            Assert.Equal(techA.Name, s.TechnicalCategoryName);
        });
        var filteredSlice = Assert.Single(filtered.Slices.Where(s => s.CategoryId == catA.Id));
        Assert.Equal(1, filteredSlice.Count);
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

        var result = await _service.GetTopOverdueProjectsAsync(ProjectLifecycleFilter.Active, null, null, 5);

        Assert.Equal(2, result.Projects.Count);
        Assert.Equal("Late A", result.Projects[0].Name);
        Assert.Equal(10, result.Projects[0].SlipDays);
        Assert.Null(result.Projects[0].TechnicalCategory);
        Assert.Equal("Late B", result.Projects[1].Name);
        Assert.Equal(5, result.Projects[1].SlipDays);
        Assert.Null(result.Projects[1].TechnicalCategory);
    }

    [Fact]
    public async Task AnalyticsEndpoints_IncludeDescendantCategories()
    {
        var parent = new ProjectCategory { Name = "Parent" };
        var childA = new ProjectCategory { Name = "Child A", Parent = parent };
        var childB = new ProjectCategory { Name = "Child B", Parent = parent };
        var other = new ProjectCategory { Name = "Other" };

        _db.ProjectCategories.AddRange(parent, childA, childB, other);
        await _db.SaveChangesAsync();

        var today = new DateOnly(2024, 6, 15);

        var descendantProject = new Project
        {
            Name = "Descendant",
            CategoryId = childA.Id,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "u",
            CreatedAt = DateTime.UtcNow,
            ProjectStages = new[]
            {
                new ProjectStage
                {
                    StageCode = "FS",
                    SortOrder = 1,
                    Status = StageStatus.Completed,
                    PlannedDue = today.AddDays(-20),
                    CompletedOn = today.AddDays(-14)
                },
                new ProjectStage
                {
                    StageCode = "DD",
                    SortOrder = 2,
                    Status = StageStatus.InProgress,
                    PlannedDue = today.AddDays(-5)
                }
            }
        };

        var otherProject = new Project
        {
            Name = "Other",
            CategoryId = other.Id,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "u",
            CreatedAt = DateTime.UtcNow,
            ProjectStages = new[]
            {
                new ProjectStage
                {
                    StageCode = "FS",
                    SortOrder = 1,
                    Status = StageStatus.Completed,
                    PlannedDue = today.AddDays(-10),
                    CompletedOn = today.AddDays(-9)
                },
                new ProjectStage
                {
                    StageCode = "DD",
                    SortOrder = 2,
                    Status = StageStatus.InProgress,
                    PlannedDue = today.AddDays(5)
                }
            }
        };

        _db.Projects.AddRange(descendantProject, otherProject);
        await _db.SaveChangesAsync();

        var stageDistribution = await _service.GetStageDistributionAsync(ProjectLifecycleFilter.All, parent.Id, null);
        var slipBuckets = await _service.GetSlipBucketsAsync(ProjectLifecycleFilter.All, parent.Id, null);
        var slipBucketProjectIds = await _service.GetProjectIdsForSlipBucketAsync(
            ProjectLifecycleFilter.All,
            parent.Id,
            null,
            "1-7");
        var topOverdue = await _service.GetTopOverdueProjectsAsync(ProjectLifecycleFilter.All, parent.Id, null, 5);

        var stageItem = Assert.Single(stageDistribution.Items, i => i.StageCode == "DD");
        Assert.Equal(1, stageItem.Count);

        var bucket = Assert.Single(slipBuckets.Buckets, b => b.Key == "1-7");
        Assert.Equal(1, bucket.Count);
        Assert.Equal(new[] { descendantProject.Id }, slipBucketProjectIds.OrderBy(id => id).ToArray());

        var overdue = Assert.Single(topOverdue.Projects);
        Assert.Equal(descendantProject.Id, overdue.ProjectId);
        Assert.Equal("DD", overdue.StageCode);
    }

    [Fact]
    public async Task SlipBucketAnalytics_FiltersByTechnicalCategory()
    {
        var techA = new TechnicalCategory { Name = "Digital" };
        var techB = new TechnicalCategory { Name = "Mechanical" };

        await _db.TechnicalCategories.AddRangeAsync(techA, techB);
        await _db.SaveChangesAsync();

        var today = new DateOnly(2024, 6, 15);

        var projectA = new Project
        {
            Name = "Tech Project A",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "creator",
            CreatedAt = DateTime.UtcNow,
            TechnicalCategoryId = techA.Id,
            ProjectStages =
            {
                new ProjectStage
                {
                    StageCode = "DD",
                    SortOrder = 1,
                    Status = StageStatus.InProgress,
                    PlannedDue = today.AddDays(-5)
                }
            }
        };

        var projectB = new Project
        {
            Name = "Tech Project B",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "creator",
            CreatedAt = DateTime.UtcNow,
            TechnicalCategoryId = techB.Id,
            ProjectStages =
            {
                new ProjectStage
                {
                    StageCode = "DD",
                    SortOrder = 1,
                    Status = StageStatus.InProgress,
                    PlannedDue = today.AddDays(-5)
                }
            }
        };

        _db.Projects.AddRange(projectA, projectB);
        await _db.SaveChangesAsync();

        var slipBuckets = await _service.GetSlipBucketsAsync(ProjectLifecycleFilter.All, null, techA.Id);
        var bucket = slipBuckets.Buckets.Single(b => b.Key == "1-7");
        Assert.Equal(1, bucket.Count);

        var slipProjectIds = await _service.GetProjectIdsForSlipBucketAsync(
            ProjectLifecycleFilter.All,
            null,
            techA.Id,
            "1-7");

        Assert.Equal(new[] { projectA.Id }, slipProjectIds.ToArray());

        var topOverdue = await _service.GetTopOverdueProjectsAsync(ProjectLifecycleFilter.All, null, techA.Id, 5);
        var overdue = Assert.Single(topOverdue.Projects);
        Assert.Equal(projectA.Id, overdue.ProjectId);
        Assert.Equal(techA.Name, overdue.TechnicalCategory);
    }

    [Fact]
    public async Task StageDistribution_FiltersByTechnicalCategory()
    {
        var techA = new TechnicalCategory { Name = "Platform" };
        var techB = new TechnicalCategory { Name = "Network" };

        await _db.TechnicalCategories.AddRangeAsync(techA, techB);
        await _db.SaveChangesAsync();

        var projectA = new Project
        {
            Name = "Tech A",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "creator",
            CreatedAt = DateTime.UtcNow,
            TechnicalCategoryId = techA.Id,
            ProjectStages =
            {
                new ProjectStage
                {
                    StageCode = "DD",
                    SortOrder = 1,
                    Status = StageStatus.InProgress
                }
            }
        };

        var projectB = new Project
        {
            Name = "Tech B",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "creator",
            CreatedAt = DateTime.UtcNow,
            TechnicalCategoryId = techB.Id,
            ProjectStages =
            {
                new ProjectStage
                {
                    StageCode = "DD",
                    SortOrder = 1,
                    Status = StageStatus.InProgress
                }
            }
        };

        _db.Projects.AddRange(projectA, projectB);
        await _db.SaveChangesAsync();

        var all = await _service.GetStageDistributionAsync(ProjectLifecycleFilter.All, null, null);
        var ddAll = Assert.Single(all.Items, i => i.StageCode == "DD");
        Assert.Equal(2, ddAll.Count);

        var filtered = await _service.GetStageDistributionAsync(ProjectLifecycleFilter.All, null, techA.Id);
        var ddFiltered = Assert.Single(filtered.Items, i => i.StageCode == "DD");
        Assert.Equal(1, ddFiltered.Count);
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
