using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class OngoingProjectsMultiSelectFilterTests
{
    // Categories: CoE (1) with sub-category CoE Short Term (2), DCD (3), Other R&D (4).
    // Projects: 10 in Tender (CoE Short Term), 20 in Development (DCD), 30 in AoN (Other R&D).
    private static async Task<OngoingProjectsReadService> CreateServiceAsync(ApplicationDbContext db)
    {
        db.ProjectCategories.AddRange(
            new ProjectCategory { Id = 1, Name = "CoE", CreatedByUserId = "seed" },
            new ProjectCategory { Id = 2, Name = "CoE Short Term", ParentId = 1, CreatedByUserId = "seed" },
            new ProjectCategory { Id = 3, Name = "DCD Projects", CreatedByUserId = "seed" },
            new ProjectCategory { Id = 4, Name = "Other R&D Projects", CreatedByUserId = "seed" });

        AddProject(db, 10, "Tender project", categoryId: 2, currentStage: StageCodes.BID);
        AddProject(db, 20, "Development project", categoryId: 3, currentStage: StageCodes.DEVP);
        AddProject(db, 30, "AoN project", categoryId: 4, currentStage: StageCodes.AON);
        await db.SaveChangesAsync();

        return new OngoingProjectsReadService(
            db,
            new WorkflowStageMetadataProvider(),
            FakeClock.ForIstDate(2026, 10, 22, 12));
    }

    private static void AddProject(ApplicationDbContext db, int id, string name, int categoryId, string currentStage)
    {
        db.Projects.Add(new Project
        {
            Id = id,
            Name = name,
            CategoryId = categoryId,
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Active
        });

        // Every stage before the current one is complete; the current one is in progress.
        var sortOrder = 0;
        foreach (var code in ProcurementWorkflow.StageCodesFor(null))
        {
            var isCurrent = string.Equals(code, currentStage, StringComparison.OrdinalIgnoreCase);
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = id,
                StageCode = code,
                SortOrder = sortOrder++,
                Status = isCurrent ? StageStatus.InProgress : StageStatus.Completed,
                CompletedOn = isCurrent ? null : new DateOnly(2026, 1, 1)
            });

            if (isCurrent)
            {
                break;
            }
        }
    }

    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task StageBuckets_MatchAnySelectedBucket()
    {
        await using var db = NewDb();
        var service = await CreateServiceAsync(db);

        var rows = await service.GetAsync(
            Array.Empty<int>(), null, null, null, new[] { "procurement", "development" }, null, CancellationToken.None);

        Assert.Equal(new[] { 10, 20 }, rows.Select(row => row.ProjectId).OrderBy(id => id));
    }

    [Fact]
    public async Task Categories_MatchAnySelectedCategory_IncludingSubCategories()
    {
        await using var db = NewDb();
        var service = await CreateServiceAsync(db);

        var rows = await service.GetAsync(
            new[] { 1, 3 }, null, null, null, Array.Empty<string>(), null, CancellationToken.None);

        Assert.Equal(new[] { 10, 20 }, rows.Select(row => row.ProjectId).OrderBy(id => id));
    }

    [Fact]
    public async Task SingleValueOverload_StillFiltersAsBefore()
    {
        await using var db = NewDb();
        var service = await CreateServiceAsync(db);

        var rows = await service.GetAsync(1, null, null, null, "procurement", null, CancellationToken.None);

        Assert.Equal(10, Assert.Single(rows).ProjectId);
    }

    [Fact]
    public async Task CategoryCounts_ApplyTheStageFilter_ButNotTheCategoryFilter()
    {
        await using var db = NewDb();
        var service = await CreateServiceAsync(db);

        var counts = await service.GetCategoryCountsAsync(
            null, null, null, new[] { "procurement", "development" }, CancellationToken.None);

        Assert.Equal(1, counts[2]);
        Assert.Equal(1, counts[3]);
        Assert.False(counts.ContainsKey(4));
    }

    [Fact]
    public async Task StageBucketCounts_ApplyTheCategoryFilter_ButNotTheStageFilter()
    {
        await using var db = NewDb();
        var service = await CreateServiceAsync(db);

        var counts = await service.GetStageBucketCountsAsync(new[] { 1, 4 }, null, null, CancellationToken.None);

        Assert.Equal((0, 1, 1, 0), (counts.Approval, counts.Aon, counts.Procurement, counts.Development));
        Assert.Equal(3, await service.CountAllAsync(CancellationToken.None));
    }
}
