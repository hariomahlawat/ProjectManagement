using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectAnalyticsServiceStageTimeTests
{
    [Fact]
    public async Task GetStageTimeInsightsAsync_BucketsByTheProjectCost()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var highCostProject = new Project
        {
            Id = 1,
            Name = "High",
            CreatedByUserId = "system",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };
        var lowCostProject = new Project
        {
            Id = 2,
            Name = "Low",
            CreatedByUserId = "system",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };

        db.Projects.AddRange(highCostProject, lowCostProject);

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                Id = 10,
                ProjectId = highCostProject.Id,
                Project = highCostProject,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 1, 1),
                CompletedOn = new DateOnly(2024, 1, 11)
            },
            new ProjectStage
            {
                Id = 11,
                ProjectId = lowCostProject.Id,
                Project = lowCostProject,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 2, 1),
                CompletedOn = new DateOnly(2024, 2, 6)
            });

        db.ProjectCommercialFacts.Add(new ProjectCommercialFact
        {
            ProjectId = highCostProject.Id,
            L1Cost = 30_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        db.ProjectPncFacts.Add(new ProjectPncFact
        {
            ProjectId = lowCostProject.Id,
            PncCost = 5_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectAnalyticsService(db, clock, new ProjectCategoryHierarchyService(db), new WorkflowStageMetadataProvider());

        var result = await service.GetStageTimeInsightsAsync();

        var fsRows = result.Rows.Where(r => r.StageKey == StageCodes.FS).ToList();
        Assert.True(fsRows.Count >= 2);

        var aboveRow = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.AboveTwoCrore);
        var belowRow = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.UpToTwoCrore);

        Assert.Equal(11, aboveRow.MedianDays);
        Assert.Equal(1, aboveRow.ProjectCount);
        Assert.Equal(6, belowRow.MedianDays);
        Assert.Equal(1, belowRow.ProjectCount);

        var hotspot = Assert.Single(result.StageHotspots);
        Assert.Equal(StageCodes.FS, hotspot.StageKey);
        Assert.Equal(8.5, hotspot.MedianDays);
        Assert.Equal(2, hotspot.ProjectCount);
    }

    [Fact]
    public async Task GetStageTimeInsightsAsync_AppliesCategoryFilter()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var designCategoryId = 10;
        var digitalCategoryId = 20;

        var designProject = new Project
        {
            Id = 10,
            Name = "Design",
            CreatedByUserId = "system",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CategoryId = designCategoryId
        };

        var digitalProject = new Project
        {
            Id = 11,
            Name = "Digital",
            CreatedByUserId = "system",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CategoryId = digitalCategoryId
        };

        db.Projects.AddRange(designProject, digitalProject);

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                Id = 100,
                ProjectId = designProject.Id,
                Project = designProject,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 4, 1),
                CompletedOn = new DateOnly(2024, 4, 11)
            },
            new ProjectStage
            {
                Id = 101,
                ProjectId = digitalProject.Id,
                Project = digitalProject,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 5, 1),
                CompletedOn = new DateOnly(2024, 5, 4)
            });

        db.ProjectCommercialFacts.Add(new ProjectCommercialFact
        {
            ProjectId = designProject.Id,
            L1Cost = 30_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        db.ProjectPncFacts.Add(new ProjectPncFact
        {
            ProjectId = digitalProject.Id,
            PncCost = 5_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectAnalyticsService(db, clock, new ProjectCategoryHierarchyService(db), new WorkflowStageMetadataProvider());

        var result = await service.GetStageTimeInsightsAsync(designCategoryId);

        var fsRows = result.Rows.Where(r => r.StageKey == StageCodes.FS).ToList();
        Assert.NotEmpty(fsRows);

        var aboveRow = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.AboveTwoCrore);
        Assert.Equal(1, aboveRow.ProjectCount);

        var belowRow = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.UpToTwoCrore);
        Assert.Equal(0, belowRow.ProjectCount);

        Assert.Equal(designCategoryId, result.SelectedCategoryId);

        var designHotspot = Assert.Single(result.StageHotspots);
        Assert.Equal(StageCodes.FS, designHotspot.StageKey);
        Assert.Equal(11, designHotspot.MedianDays);
        Assert.Equal(1, designHotspot.ProjectCount);
    }

    [Fact]
    public async Task GetStageTimeInsightsAsync_ExcludesTotStage()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var project = new Project
        {
            Id = 50,
            Name = "Transfer",
            CreatedByUserId = "system",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };

        db.Projects.Add(project);

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                Id = 500,
                ProjectId = project.Id,
                Project = project,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 6, 1),
                CompletedOn = new DateOnly(2024, 6, 4)
            },
            new ProjectStage
            {
                Id = 501,
                ProjectId = project.Id,
                Project = project,
                StageCode = StageCodes.TOT,
                SortOrder = 140,
                ActualStart = new DateOnly(2024, 7, 1),
                CompletedOn = new DateOnly(2024, 7, 10)
            });

        db.ProjectCommercialFacts.Add(new ProjectCommercialFact
        {
            ProjectId = project.Id,
            L1Cost = 15_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectAnalyticsService(db, clock, new ProjectCategoryHierarchyService(db), new WorkflowStageMetadataProvider());

        var result = await service.GetStageTimeInsightsAsync();

        Assert.DoesNotContain(result.Rows, row => string.Equals(row.StageKey, StageCodes.TOT, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.StageHotspots, point => string.Equals(point.StageKey, StageCodes.TOT, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.StageHotspots, point => point.StageKey == StageCodes.FS);
    }

    [Fact]
    public async Task GetStageTimeInsightsAsync_DoesNotPadTotStage()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var project = new Project
        {
            Id = 60,
            Name = "Padding", 
            CreatedByUserId = "system",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };

        db.Projects.Add(project);

        db.ProjectStages.Add(new ProjectStage
        {
            Id = 601,
            ProjectId = project.Id,
            Project = project,
            StageCode = StageCodes.FS,
            SortOrder = 1,
            ActualStart = new DateOnly(2024, 5, 1),
            CompletedOn = new DateOnly(2024, 5, 5)
        });

        db.ProjectCommercialFacts.Add(new ProjectCommercialFact
        {
            ProjectId = project.Id,
            L1Cost = 12_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectAnalyticsService(db, clock, new ProjectCategoryHierarchyService(db), new WorkflowStageMetadataProvider());

        var result = await service.GetStageTimeInsightsAsync();

        Assert.DoesNotContain(result.Rows, row => string.Equals(row.StageKey, StageCodes.TOT, StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public async Task GetStageTimeInsightsAsync_KeepsUncostedProjectsInHotspotsButNotInBands()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var costed = new Project { Id = 1, Name = "Costed", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed };
        var uncosted = new Project { Id = 2, Name = "No cost yet", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Active };
        db.Projects.AddRange(costed, uncosted);

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                Id = 10,
                ProjectId = costed.Id,
                Project = costed,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 1, 1),
                CompletedOn = new DateOnly(2024, 1, 10)
            },
            new ProjectStage
            {
                Id = 11,
                ProjectId = uncosted.Id,
                Project = uncosted,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 2, 1),
                CompletedOn = new DateOnly(2024, 2, 20)
            });

        db.ProjectPncFacts.Add(new ProjectPncFact
        {
            ProjectId = costed.Id,
            PncCost = 5_000_000m,
            CreatedByUserId = "seed",
            CreatedOnUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var service = new ProjectAnalyticsService(db, FakeClock.AtUtc(DateTimeOffset.UtcNow), new ProjectCategoryHierarchyService(db), new WorkflowStageMetadataProvider());

        var result = await service.GetStageTimeInsightsAsync();

        // All projects: both finished stages count, 10 and 20 days.
        var hotspot = Assert.Single(result.StageHotspots);
        Assert.Equal(StageCodes.FS, hotspot.StageKey);
        Assert.Equal(2, hotspot.ProjectCount);
        Assert.Equal(15, hotspot.MedianDays);

        // By cost band: only the costed project has a band.
        var fsRows = result.Rows.Where(r => r.StageKey == StageCodes.FS).ToList();
        var belowRow = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.UpToTwoCrore);
        Assert.Equal(1, belowRow.ProjectCount);
        Assert.Equal(10, belowRow.MedianDays);
        var aboveRow = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.AboveTwoCrore);
        Assert.Equal(0, aboveRow.ProjectCount);
    }

    [Fact]
    public async Task GetStageTimeInsightsAsync_CategoryFilterIncludesSubCategories()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        var coeRoot = new ProjectCategory { Id = 1, Name = "CoE" };
        var coeShortTerm = new ProjectCategory { Id = 2, Name = "CoE Short Term", ParentId = coeRoot.Id };
        var other = new ProjectCategory { Id = 3, Name = "Other R&D Projects" };
        db.ProjectCategories.AddRange(coeRoot, coeShortTerm, other);

        var onRoot = new Project { Id = 1, Name = "On root", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed, CategoryId = coeRoot.Id };
        var onChild = new Project { Id = 2, Name = "On child", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed, CategoryId = coeShortTerm.Id };
        var elsewhere = new Project { Id = 3, Name = "Elsewhere", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed, CategoryId = other.Id };
        db.Projects.AddRange(onRoot, onChild, elsewhere);

        var id = 100;
        foreach (var (project, days) in new[] { (onRoot, 10), (onChild, 20), (elsewhere, 90) })
        {
            db.ProjectStages.Add(new ProjectStage
            {
                Id = id++,
                ProjectId = project.Id,
                Project = project,
                StageCode = StageCodes.FS,
                SortOrder = 1,
                ActualStart = new DateOnly(2024, 1, 1),
                CompletedOn = new DateOnly(2024, 1, 1).AddDays(days - 1)
            });
        }

        await db.SaveChangesAsync();

        var service = new ProjectAnalyticsService(db, FakeClock.AtUtc(DateTimeOffset.UtcNow), new ProjectCategoryHierarchyService(db), new WorkflowStageMetadataProvider());

        var result = await service.GetStageTimeInsightsAsync(coeRoot.Id);

        var hotspot = Assert.Single(result.StageHotspots);
        Assert.Equal(2, hotspot.ProjectCount);
        Assert.Equal(15, hotspot.MedianDays);
        Assert.Equal(coeRoot.Id, result.SelectedCategoryId);
    }

    [Fact]
    public async Task GetStageTimeInsightsAsync_ExactlyTwoCroreIsInTheLowerBand()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var atBoundary = new Project { Id = 1, Name = "Exactly 2 Cr", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed };
        var justAbove = new Project { Id = 2, Name = "2 Cr and a rupee", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed };
        db.Projects.AddRange(atBoundary, justAbove);
        db.ProjectStages.AddRange(
            new ProjectStage { Id = 1, ProjectId = 1, Project = atBoundary, StageCode = StageCodes.FS, SortOrder = 1, ActualStart = new DateOnly(2024, 1, 1), CompletedOn = new DateOnly(2024, 1, 10) },
            new ProjectStage { Id = 2, ProjectId = 2, Project = justAbove, StageCode = StageCodes.FS, SortOrder = 1, ActualStart = new DateOnly(2024, 1, 1), CompletedOn = new DateOnly(2024, 1, 30) });
        db.ProjectCommercialFacts.AddRange(
            new ProjectCommercialFact { ProjectId = 1, L1Cost = 20_000_000m, CreatedByUserId = "seed", CreatedOnUtc = DateTime.UtcNow },
            new ProjectCommercialFact { ProjectId = 2, L1Cost = 20_000_001m, CreatedByUserId = "seed", CreatedOnUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = new ProjectAnalyticsService(db, FakeClock.AtUtc(DateTimeOffset.UtcNow), new ProjectCategoryHierarchyService(db), new WorkflowStageMetadataProvider());

        var result = await service.GetStageTimeInsightsAsync();

        var fsRows = result.Rows.Where(r => r.StageKey == StageCodes.FS).ToList();
        var lower = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.UpToTwoCrore);
        var upper = fsRows.Single(r => r.Bucket == StageTimeBucketKeys.AboveTwoCrore);
        Assert.Equal(10, lower.MedianDays);
        Assert.Equal(1, lower.ProjectCount);
        Assert.Equal(30, upper.MedianDays);
        Assert.Equal(1, upper.ProjectCount);
    }

    [Fact]
    public async Task GetStageTimeInsightsAsync_SplitsByRootCategory_AndKeepsUncostedProjects()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var dcd = new ProjectCategory { Id = 1, Name = "DCD Projects" };
        var dcdChild = new ProjectCategory { Id = 2, Name = "AR / VR", ParentId = dcd.Id };
        var coe = new ProjectCategory { Id = 3, Name = "CoE" };
        db.ProjectCategories.AddRange(dcd, dcdChild, coe);

        var onDcdChild = new Project { Id = 1, Name = "DCD via child", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed, CategoryId = dcdChild.Id };
        var onDcdRoot = new Project { Id = 2, Name = "DCD on root", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Active, CategoryId = dcd.Id };
        var onCoe = new Project { Id = 3, Name = "CoE", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed, CategoryId = coe.Id };
        var uncategorized = new Project { Id = 4, Name = "No category", CreatedByUserId = "system", LifecycleStatus = ProjectLifecycleStatus.Completed };
        db.Projects.AddRange(onDcdChild, onDcdRoot, onCoe, uncategorized);

        var id = 1;
        foreach (var (project, days) in new[] { (onDcdChild, 10), (onDcdRoot, 20), (onCoe, 40), (uncategorized, 80) })
        {
            db.ProjectStages.Add(new ProjectStage { Id = id++, ProjectId = project.Id, Project = project, StageCode = StageCodes.FS, SortOrder = 1, ActualStart = new DateOnly(2024, 1, 1), CompletedOn = new DateOnly(2024, 1, 1).AddDays(days - 1) });
        }
        await db.SaveChangesAsync(); // no cost figures at all: the category split must not depend on them

        var service = new ProjectAnalyticsService(db, FakeClock.AtUtc(DateTimeOffset.UtcNow), new ProjectCategoryHierarchyService(db), new WorkflowStageMetadataProvider());

        var result = await service.GetStageTimeInsightsAsync();

        var fs = result.CategoryRows.Where(r => r.StageKey == StageCodes.FS).ToDictionary(r => r.Bucket);
        Assert.Equal(new[] { "CoE", "DCD Projects", "Uncategorized" }, fs.Keys.OrderBy(k => k));
        Assert.Equal(15, fs["DCD Projects"].MedianDays); // the child category rolls up to its root
        Assert.Equal(2, fs["DCD Projects"].ProjectCount);
        Assert.Equal(1, fs["DCD Projects"].OngoingProjectCount);
        Assert.Equal(40, fs["CoE"].MedianDays);
        Assert.Equal(80, fs["Uncategorized"].MedianDays);
        // Every other workflow stage is padded at zero for each category present, as the cost bands are.
        Assert.Equal(3, result.CategoryRows.Count(r => r.StageKey == StageCodes.IPA && r.ProjectCount == 0));
        Assert.Empty(result.Rows.Where(r => r.ProjectCount > 0)); // no cost figure: no project in either band
    }
}
