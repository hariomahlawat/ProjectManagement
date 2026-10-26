using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Analytics;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>The Insights mode's headline cards: the three slowest stages and the portfolio median.</summary>
public sealed class InsightsHeadlinesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public InsightsHeadlinesTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    private IndexModel Page(params (string Code, double MedianDays, int Projects)[] stages) =>
        new(_db, new StubInsights(stages), new PortfolioAnalyticsService(_db, new WorkflowStageMetadataProvider()));

    [Fact]
    public async Task Headlines_AreTheThreeSlowestStagesAndThePortfolioMedian()
    {
        var page = Page((StageCodes.DEVP, 75, 12), (StageCodes.AON, 36, 30), (StageCodes.SOW, 24, 2), (StageCodes.FS, 21, 40), (StageCodes.SO, 6, 9));

        await page.OnGetAsync("insights", CancellationToken.None);

        Assert.Equal(new[] { "Slowest stage", "Second slowest", "Third slowest", "Portfolio median" }, page.Headlines.Select(f => f.Label));
        Assert.Equal(new[] { "Development", "Acceptance of Necessity", "SOW Vetting", "24" }, page.Headlines.Select(f => f.Value));
        // Portfolio median is the median of the five stage medians (24), so Development is 3.1× it.
        Assert.Equal("75 days median · 3.1× portfolio median · 12 projects", page.Headlines[0].Hint);
        Assert.Equal("24 days median · 1.0× portfolio median · 2 projects, small sample", page.Headlines[2].Hint);
        Assert.Equal("days per stage, across 5 stages measured", page.Headlines[3].Hint);
    }

    [Fact]
    public async Task Headlines_FillMissingRanksWithADash()
    {
        var page = Page((StageCodes.DEVP, 75, 12));

        await page.OnGetAsync("insights", CancellationToken.None);

        Assert.Equal(new[] { "Development", "—", "—", "75" }, page.Headlines.Select(f => f.Value));
        Assert.Null(page.Headlines[1].Hint);
        Assert.Equal("days per stage, across 1 stage measured", page.Headlines[3].Hint);
    }

    [Fact]
    public async Task Headlines_WithNoFinishedStages_SayNothingIsMeasuredYet()
    {
        var page = Page();

        await page.OnGetAsync("insights", CancellationToken.None);

        Assert.Equal(new[] { "—", "—", "—", "—" }, page.Headlines.Select(f => f.Value));
        Assert.Equal("no finished stages yet", page.Headlines[3].Hint);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class StubInsights : IProjectAnalyticsService
    {
        private readonly IReadOnlyList<StageHotspotPointVm> _points;

        public StubInsights((string Code, double MedianDays, int Projects)[] stages)
        {
            _points = stages
                .Select((stage, index) => new StageHotspotPointVm
                {
                    StageKey = stage.Code,
                    StageName = StageCodes.DisplayNameOf(stage.Code),
                    StageOrder = index,
                    MedianDays = stage.MedianDays,
                    AverageDays = stage.MedianDays,
                    ProjectCount = stage.Projects
                })
                .ToList();
        }

        public Task<StageTimeInsightsVm> GetStageTimeInsightsAsync(int? categoryId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StageTimeInsightsVm { StageHotspots = _points, SelectedCategoryId = categoryId });

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
    }
}
