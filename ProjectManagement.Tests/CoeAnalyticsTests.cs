using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Analytics;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Tests;

/// <summary>
/// The Ongoing and CoE modes: the aggregation in <see cref="PortfolioAnalyticsService"/> and the
/// page rules on top of it (headline hints, the select-all filter rule, the switcher badges).
/// </summary>
public sealed class CoeAnalyticsTests : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly PortfolioAnalyticsService _service;
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

        _service = new PortfolioAnalyticsService(_db, new WorkflowStageMetadataProvider());
        _pageModel = new IndexModel(_db, new StubProjectAnalyticsService(), _service);
    }

    [Fact]
    public async Task OngoingAnalytics_UsesCanonicalFirstStageAndRecursiveRootCategoryForNewProject()
    {
        var dcdRoot = new ProjectCategory { Name = "DCD Projects", SortOrder = 1 };
        var arVr = new ProjectCategory { Name = "AR / VR", Parent = dcdRoot, SortOrder = 1 };
        var medical = new ProjectCategory { Name = "Medical Simulation", Parent = arVr, SortOrder = 1 };
        _db.ProjectCategories.AddRange(dcdRoot, arVr, medical);

        _db.Projects.Add(new Project
        {
            Name = "VR CMC (Philippines 2026)",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            WorkflowVersion = ProcurementWorkflow.VersionV1,
            Category = medical
        });
        await _db.SaveChangesAsync();

        await _pageModel.OnGetAsync("ongoing", CancellationToken.None);

        var stagePoint = Assert.Single(_pageModel.Ongoing!.ByStageByParentCategory);
        Assert.Equal(StageCodes.FS, stagePoint.StageCode);
        Assert.Equal("DCD Projects", stagePoint.CategoryName);
        Assert.Equal(1, stagePoint.Count);

        var stageBoard = Assert.Single(_pageModel.Ongoing.StageBoard);
        Assert.Equal(StageCodes.FS, stageBoard.StageCode);
        var category = Assert.Single(stageBoard.Categories);
        Assert.Equal("DCD Projects", category.ParentCategoryName);
    }

    [Fact]
    public async Task BuildCoeAsync_ComputesLifecycleStagesAndSubcategories()
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

        var result = await _service.BuildCoeAsync(Today, CancellationToken.None);

        Assert.Equal(3, result.TotalCoeProjects);

        Assert.Equal(StageCodes.All.Length, result.ByStage.Count);
        var stageBucket = Assert.Single(result.ByStage
            .Where(bucket => bucket.StageName == StageCodes.DisplayNameOf(StageCodes.IPA)));
        Assert.Equal(1, stageBucket.ProjectCount);
        Assert.All(result.ByStage.Where(bucket => bucket.StageName != StageCodes.DisplayNameOf(StageCodes.IPA)), bucket =>
        {
            Assert.Equal(0, bucket.ProjectCount);
        });

        var aiBucket = Assert.Single(result.SubcategoriesByLifecycle.Where(s => s.Name == "AI Innovation"));
        Assert.Equal(1, aiBucket.Ongoing);
        Assert.Equal(0, aiBucket.Completed);
        Assert.Equal(0, aiBucket.Cancelled);

        var roboticsBucket = Assert.Single(result.SubcategoriesByLifecycle.Where(s => s.Name == "Robotics"));
        Assert.Equal(0, roboticsBucket.Ongoing);
        Assert.Equal(1, roboticsBucket.Completed);
        Assert.Equal(1, roboticsBucket.Cancelled);
    }

    [Fact]
    public async Task BuildCoeAsync_ReturnsAllSubcategoriesWithoutGrouping()
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

        var result = await _service.BuildCoeAsync(Today, CancellationToken.None);

        Assert.Equal(12, result.TotalCoeProjects);
        Assert.Equal(11, result.SubcategoriesByLifecycle.Count);
        var otherBucket = Assert.Single(result.SubcategoriesByLifecycle.Where(bucket => bucket.Name == "Other"));
        Assert.Equal(0, otherBucket.Ongoing);
        Assert.Equal(2, otherBucket.Completed);
        Assert.Equal(0, otherBucket.Cancelled);
        var orderedNames = result.SubcategoriesByLifecycle
            .Where(bucket => bucket.Name != "Other")
            .Select(b => b.Name)
            .ToList();
        var sortedNames = orderedNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        Assert.Equal(sortedNames, orderedNames);
    }

    [Fact]
    public async Task BuildCoeAsync_NoCoeCategoriesReturnsEmpty()
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

        var result = await _service.BuildCoeAsync(Today, CancellationToken.None);

        Assert.False(result.HasCoeProjects);
        Assert.Empty(result.ByStage);
        Assert.False(result.HasSubcategoryBreakdown);
    }

    [Fact]
    public async Task OngoingAnalytics_ParentCategoryFilter_HeadlineTotalFollowsTheFilter()
    {
        var dcd = new ProjectCategory { Name = "DCD Projects", SortOrder = 1 };
        var other = new ProjectCategory { Name = "Other R&D Projects", SortOrder = 2 };
        _db.ProjectCategories.AddRange(dcd, other);
        await _db.SaveChangesAsync();

        _db.Projects.AddRange(
            new Project { Name = "DCD one", CreatedByUserId = "seed", LifecycleStatus = ProjectLifecycleStatus.Active, WorkflowVersion = ProcurementWorkflow.VersionV1, CategoryId = dcd.Id },
            new Project { Name = "Other one", CreatedByUserId = "seed", LifecycleStatus = ProjectLifecycleStatus.Active, WorkflowVersion = ProcurementWorkflow.VersionV1, CategoryId = other.Id },
            new Project { Name = "Other two", CreatedByUserId = "seed", LifecycleStatus = ProjectLifecycleStatus.Active, WorkflowVersion = ProcurementWorkflow.VersionV1, CategoryId = other.Id });
        await _db.SaveChangesAsync();

        _pageModel.OngoingStageParentCategoryIds = new List<int> { dcd.Id };
        await _pageModel.OnGetAsync("ongoing", CancellationToken.None);

        Assert.True(_pageModel.IsOngoingFiltered);
        Assert.Equal(3, _pageModel.OngoingCount); // the switcher badge is the portfolio total
        Assert.Equal(1, _pageModel.Ongoing!.TotalOngoingProjects); // the figures follow the filter
        Assert.Equal(1, _pageModel.Ongoing.TopStageCount);
        var figure = _pageModel.Headlines.Single(f => f.Label == "Ongoing projects");
        Assert.Equal("1", figure.Value);
        Assert.Equal("of 3 in the portfolio", figure.Hint);
    }

    [Fact]
    public async Task OngoingAnalytics_NoFilter_HeadlineTotalIsThePortfolio()
    {
        var dcd = new ProjectCategory { Name = "DCD Projects", SortOrder = 1 };
        _db.ProjectCategories.Add(dcd);
        await _db.SaveChangesAsync();
        _db.Projects.AddRange(
            new Project { Name = "A", CreatedByUserId = "seed", LifecycleStatus = ProjectLifecycleStatus.Active, WorkflowVersion = ProcurementWorkflow.VersionV1, CategoryId = dcd.Id },
            new Project { Name = "B", CreatedByUserId = "seed", LifecycleStatus = ProjectLifecycleStatus.Active, WorkflowVersion = ProcurementWorkflow.VersionV1 });
        await _db.SaveChangesAsync();

        await _pageModel.OnGetAsync("ongoing", CancellationToken.None);

        Assert.False(_pageModel.IsOngoingFiltered);
        Assert.Equal(2, _pageModel.Ongoing!.TotalOngoingProjects);
        Assert.Equal("in the portfolio", _pageModel.Headlines.Single(f => f.Label == "Ongoing projects").Hint);
    }

    [Fact]
    public async Task OngoingAnalytics_SelectAllParentCategories_IsNoFilterAndKeepsUncategorizedProjects()
    {
        var dcd = new ProjectCategory { Name = "DCD Projects", SortOrder = 1 };
        var other = new ProjectCategory { Name = "Other R&D Projects", SortOrder = 2 };
        _db.ProjectCategories.AddRange(dcd, other);
        await _db.SaveChangesAsync();
        _db.Projects.AddRange(
            new Project { Name = "DCD one", CreatedByUserId = "seed", LifecycleStatus = ProjectLifecycleStatus.Active, WorkflowVersion = ProcurementWorkflow.VersionV1, CategoryId = dcd.Id },
            new Project { Name = "No category", CreatedByUserId = "seed", LifecycleStatus = ProjectLifecycleStatus.Active, WorkflowVersion = ProcurementWorkflow.VersionV1 });
        await _db.SaveChangesAsync();

        _pageModel.OngoingStageParentCategoryIds = new List<int> { dcd.Id, other.Id };
        await _pageModel.OnGetAsync("ongoing", CancellationToken.None);

        Assert.False(_pageModel.IsOngoingFiltered);
        Assert.Empty(_pageModel.ActiveOngoingStageParentCategoryIds);
        Assert.Equal(2, _pageModel.Ongoing!.TotalOngoingProjects);
        Assert.Equal(2, _pageModel.Ongoing.StageBoard.Sum(stage => stage.StageCount));
        Assert.Contains(_pageModel.Ongoing.ByStageByParentCategory, point => point.CategoryName == "Uncategorized");
    }

    [Fact]
    public async Task CountCoeProjectsAsync_MatchesTheFullAggregationTotal()
    {
        var coeRoot = new ProjectCategory { Name = "CoE Programs" };
        var ai = new ProjectCategory { Name = "AI Innovation", Parent = coeRoot };
        var general = new ProjectCategory { Name = "General" };
        _db.ProjectCategories.AddRange(coeRoot, ai, general);
        await _db.SaveChangesAsync();

        _db.Projects.AddRange(
            new Project { Name = "CoE active", CategoryId = ai.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "user" },
            new Project { Name = "CoE completed", CategoryId = coeRoot.Id, LifecycleStatus = ProjectLifecycleStatus.Completed, CreatedByUserId = "user" },
            new Project { Name = "CoE archived", CategoryId = ai.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "user", IsArchived = true },
            new Project { Name = "General", CategoryId = general.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "user" });
        await _db.SaveChangesAsync();

        var count = await _service.CountCoeProjectsAsync(CancellationToken.None);
        var full = await _service.BuildCoeAsync(Today, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(full.TotalCoeProjects, count);
    }

    [Fact]
    public async Task CountCoeProjectsAsync_NoCoeCategoriesIsZero()
    {
        _db.ProjectCategories.Add(new ProjectCategory { Name = "General" });
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _service.CountCoeProjectsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task OngoingTab_CoeBadgeIsTheCoeTotal_AndCoeTabHydratesTheMode()
    {
        var coeRoot = new ProjectCategory { Name = "Centre of Excellence" };
        _db.ProjectCategories.Add(coeRoot);
        await _db.SaveChangesAsync();
        _db.Projects.Add(new Project { Name = "CoE one", CategoryId = coeRoot.Id, LifecycleStatus = ProjectLifecycleStatus.Active, CreatedByUserId = "user", WorkflowVersion = ProcurementWorkflow.VersionV1 });
        await _db.SaveChangesAsync();

        await _pageModel.OnGetAsync("ongoing", CancellationToken.None);
        Assert.Equal(1, _pageModel.CoeCount);
        Assert.Null(_pageModel.Coe);

        await _pageModel.OnGetAsync("coe", CancellationToken.None);
        Assert.Equal(1, _pageModel.CoeCount);
        Assert.NotNull(_pageModel.Coe);
        Assert.Equal(1, _pageModel.Coe!.OngoingCount);
    }

    [Fact]
    public async Task BuildOngoingAsync_MeasuresDaysInStageAgainstTheReferenceDate()
    {
        var root = new ProjectCategory { Name = "DCD Projects", SortOrder = 1 };
        _db.ProjectCategories.Add(root);
        await _db.SaveChangesAsync();
        var started = new DateOnly(2026, 1, 1);
        _db.Projects.Add(new Project
        {
            Name = "In FS",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            WorkflowVersion = ProcurementWorkflow.VersionV1,
            CategoryId = root.Id,
            ProjectStages =
            {
                new ProjectStage { StageCode = StageCodes.FS, SortOrder = 10, Status = StageStatus.InProgress, ActualStart = started }
            }
        });
        await _db.SaveChangesAsync();

        var tenDaysLater = await _service.BuildOngoingAsync(Array.Empty<int>(), new[] { root.Id }, started.AddDays(10), CancellationToken.None);
        var pastThreshold = await _service.BuildOngoingAsync(Array.Empty<int>(), new[] { root.Id }, started.AddDays(OngoingAnalyticsVm.LongStageThresholdDays + 20), CancellationToken.None);

        Assert.Equal(1, tenDaysLater.TotalOngoingProjects);
        Assert.Equal(StageCodes.FS, Assert.Single(tenDaysLater.StageBoard).StageCode);
        Assert.Equal(0, tenDaysLater.ProjectsPastThreshold);
        Assert.Equal(1, pastThreshold.ProjectsPastThreshold);
        Assert.True(pastThreshold.MedianDaysInCurrentStage > tenDaysLater.MedianDaysInCurrentStage);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    /// <summary>The Insights service is not under test here; the page only needs an instance.</summary>
    private sealed class StubProjectAnalyticsService : IProjectAnalyticsService
    {
        public Task<CategoryShareResult> GetCategoryShareAsync(ProjectLifecycleFilter lifecycle, int? categoryId = null, int? technicalCategoryId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoryShareResult(Array.Empty<CategoryShareSlice>(), 0));

        public Task<StageDistributionResult> GetStageDistributionAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StageDistributionResult(Array.Empty<StageDistributionItem>(), lifecycle));

        public Task<SlipBucketResult> GetSlipBucketsAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SlipBucketResult(Array.Empty<SlipBucketItem>()));

        public Task<IReadOnlyCollection<int>> GetProjectIdsForSlipBucketAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, string bucketKey, CancellationToken cancellationToken = default, IReadOnlyCollection<int>? expandedCategoryIds = null) =>
            Task.FromResult<IReadOnlyCollection<int>>(Array.Empty<int>());

        public Task<TopOverdueProjectsResult> GetTopOverdueProjectsAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TopOverdueProjectsResult(Array.Empty<TopOverdueProject>()));

        public Task<StageTimeInsightsVm> GetStageTimeInsightsAsync(int? categoryId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StageTimeInsightsVm { SelectedCategoryId = categoryId });
    }
}
