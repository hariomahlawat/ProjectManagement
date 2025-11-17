using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Analytics;

namespace ProjectManagement.Tests;

public sealed class CoeAnalyticsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly IndexModel _pageModel;

    public CoeAnalyticsTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        _pageModel = new IndexModel(_db);
    }

    [Fact]
    public async Task BuildCoeAnalyticsAsync_ComputesLifecycleStagesAndSubcategories()
    {
        var coeRoot = new ProjectCategory { Name = "CoE Programs" };
        var ai = new ProjectCategory { Name = "AI Innovation", Parent = coeRoot };
        var robotics = new ProjectCategory { Name = "Robotics", Parent = coeRoot };
        _db.ProjectCategories.AddRange(coeRoot, ai, robotics);
        await _db.SaveChangesAsync();

        var activeProject = new Project
        {
            Name = "Active",
            CategoryId = ai.Id,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "user",
            CreatedAt = DateTime.UtcNow,
            ProjectStages =
            {
                new ProjectStage
                {
                    StageCode = StageCodes.FS,
                    SortOrder = 10,
                    Status = StageStatus.Completed,
                    CompletedOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7))
                },
                new ProjectStage
                {
                    StageCode = StageCodes.IPA,
                    SortOrder = 20,
                    Status = StageStatus.InProgress
                }
            }
        };

        var completedProject = new Project
        {
            Name = "Completed",
            CategoryId = robotics.Id,
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CreatedByUserId = "user",
            CreatedAt = DateTime.UtcNow
        };

        var cancelledProject = new Project
        {
            Name = "Cancelled",
            CategoryId = robotics.Id,
            LifecycleStatus = ProjectLifecycleStatus.Cancelled,
            CreatedByUserId = "user",
            CreatedAt = DateTime.UtcNow
        };

        _db.Projects.AddRange(activeProject, completedProject, cancelledProject);
        await _db.SaveChangesAsync();

        var result = await _pageModel.BuildCoeAnalyticsAsync(CancellationToken.None);

        Assert.Equal(3, result.TotalCoeProjects);
        Assert.Equal(1, result.ByLifecycle.Single(b => b.LifecycleStatus == "Ongoing").ProjectCount);
        Assert.Equal(1, result.ByLifecycle.Single(b => b.LifecycleStatus == "Completed").ProjectCount);
        Assert.Equal(1, result.ByLifecycle.Single(b => b.LifecycleStatus == "Cancelled").ProjectCount);

        var stageBucket = Assert.Single(result.ByStage);
        Assert.Equal(StageCodes.DisplayNameOf(StageCodes.IPA), stageBucket.StageName);
        Assert.Equal(1, stageBucket.ProjectCount);

        var aiBucket = Assert.Single(result.SubcategoriesByLifecycle.Where(s => s.Name == "AI Innovation"));
        Assert.Equal(1, aiBucket.OngoingCount);
        Assert.Equal(0, aiBucket.CompletedCount);
        Assert.Equal(0, aiBucket.CancelledCount);

        var roboticsBucket = Assert.Single(result.SubcategoriesByLifecycle.Where(s => s.Name == "Robotics"));
        Assert.Equal(0, roboticsBucket.OngoingCount);
        Assert.Equal(1, roboticsBucket.CompletedCount);
        Assert.Equal(1, roboticsBucket.CancelledCount);
    }

    [Fact]
    public async Task BuildCoeAnalyticsAsync_GroupsOverflowSubcategoriesIntoOther()
    {
        var coeRoot = new ProjectCategory { Name = "Centres of Excellence" };
        _db.ProjectCategories.Add(coeRoot);
        await _db.SaveChangesAsync();

        var children = Enumerable.Range(0, 12)
            .Select(i => new ProjectCategory { Name = $"Sub {i:D2}", Parent = coeRoot })
            .ToList();
        _db.ProjectCategories.AddRange(children);
        await _db.SaveChangesAsync();

        foreach (var child in children)
        {
            _db.Projects.Add(new Project
            {
                Name = $"Project {child.Name}",
                CategoryId = child.Id,
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CreatedByUserId = "user",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        var result = await _pageModel.BuildCoeAnalyticsAsync(CancellationToken.None);

        Assert.Equal(12, result.TotalCoeProjects);
        var otherBucket = Assert.Single(result.SubcategoriesByLifecycle.Where(b => b.Name == "Other"));
        Assert.Equal(2, otherBucket.Total);
        Assert.True(result.SubcategoriesByLifecycle.Count >= 11);
    }

    [Fact]
    public async Task BuildCoeAnalyticsAsync_NoCoeCategoriesReturnsEmpty()
    {
        var generalCategory = new ProjectCategory { Name = "General" };
        _db.ProjectCategories.Add(generalCategory);
        await _db.SaveChangesAsync();

        _db.Projects.Add(new Project
        {
            Name = "General Project",
            CategoryId = generalCategory.Id,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "user",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _pageModel.BuildCoeAnalyticsAsync(CancellationToken.None);

        Assert.False(result.HasCoeProjects);
        Assert.Empty(result.ByStage);
        Assert.All(result.ByLifecycle, bucket => Assert.Equal(0, bucket.ProjectCount));
        Assert.False(result.HasSubcategoryBreakdown);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
