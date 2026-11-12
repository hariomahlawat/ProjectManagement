using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Dashboard;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>The dashboard's Analytics card: what it takes from each Analytics mode, and how it caches.</summary>
public class DashboardAnalyticsServiceTests
{
    [Fact]
    public async Task GetAsync_TakesTheHeadlineFiguresOfEachMode()
    {
        var service = Build(out _, out _);

        var result = await service.GetAsync();

        var arpp = Assert.IsType<DashboardArppVm>(result.Arpp);
        Assert.Equal("2026-27", arpp.FinancialYearDisplay);
        Assert.Equal((38, 4, 13, 13, 7), (arpp.ListedCount, arpp.CompletedCount, arpp.SupplyOrderCount, arpp.AonToSupplyOrderCount, arpp.PreAonCount));
        Assert.Equal((11, 22, 5, 1, 1), (arpp.NewCount, arpp.CommittedLiabilityCount, arpp.CarryForwardCount, arpp.CancelledCount, arpp.DelistedCount));

        var coe = Assert.IsType<DashboardCoeVm>(result.Coe);
        Assert.Equal((18, 15, 2, 1), (coe.Total, coe.Ongoing, coe.Completed, coe.Cancelled));
        // Three sub-category rows fit the card; the card says there are more. It gives no number: the
        // chart rows it reads are capped, with the tail merged into one "Other" row.
        Assert.Equal(new[] { "CoE Mid Term", "CoE Short Term", "CoE Long Term" }, coe.Subcategories.Select(row => row.Name));
        Assert.True(coe.HasMoreSubcategories);

        var completed = Assert.IsType<DashboardCompletedVm>(result.Completed);
        Assert.Equal((158, 7), (completed.Total, completed.ThisYear));
        Assert.Equal(12.2, completed.RecentAverage!.PerYear);
        // Ten years ending with the current one; a year without a completion is there as zero.
        Assert.Equal(Enumerable.Range(DateTime.UtcNow.AddHours(5.5).Year - 9, 10), completed.LastYears.Select(point => point.Year));
    }

    [Fact]
    public async Task GetAsync_SaysNothingToShowWhenOnlyCompletedHasFigures()
    {
        var service = Build(out var portfolio, out var arpp);
        arpp.Result = new ArppAnalyticsVm();
        portfolio.Coe = new CoeAnalyticsVm();
        var insights = new StubInsights { Empty = true };
        service = new DashboardAnalyticsService(new MemoryCache(new MemoryCacheOptions()), portfolio, insights, arpp, NullLogger<DashboardAnalyticsService>.Instance);

        var result = await service.GetAsync();

        // Completed has its own card on the dashboard, so the Analytics card shows its empty state.
        Assert.NotNull(result.Completed);
        Assert.False(result.HasAnything);
    }

    [Fact]
    public async Task GetAsync_NamesTheThreeSlowestMeasuredStagesAgainstThePortfolioMedian()
    {
        var service = Build(out _, out _);

        var result = await service.GetAsync();

        // Medians 75, 36, 24, 20, 10 → portfolio median 24. The stage with no finished project is not ranked.
        Assert.Equal(new[] { "Development", "Acceptance of Necessity", "SOW Vetting" }, result.SlowestStages.Select(stage => stage.StageName));
        Assert.Equal(new[] { 75, 36, 24 }, result.SlowestStages.Select(stage => stage.MedianDays));
        Assert.Equal(75d / 24d, result.SlowestStages[0].RatioToPortfolioMedian, 3);
    }

    [Fact]
    public async Task GetAsync_ReadsOnceWhileTheResultIsCached()
    {
        var service = Build(out var portfolio, out _);

        await service.GetAsync();
        await service.GetAsync();

        Assert.Equal(1, portfolio.CompletedCalls);
    }

    [Fact]
    public async Task GetAsync_KeepsTheOtherPartsWhenOneFails_AndTriesAgainNextTime()
    {
        var service = Build(out var portfolio, out var arpp);
        arpp.Fail = true;

        var first = await service.GetAsync();

        Assert.Null(first.Arpp);
        Assert.NotNull(first.Coe);
        Assert.NotNull(first.Completed);

        arpp.Fail = false;
        var second = await service.GetAsync();

        Assert.NotNull(second.Arpp);
        Assert.Equal(2, portfolio.CompletedCalls);
    }

    [Fact]
    public async Task GetAsync_LeavesOutArppAndCoeWhenTheyHaveNothing()
    {
        var service = Build(out var portfolio, out var arpp);
        arpp.Result = new ArppAnalyticsVm { FinancialYearDisplay = "2026-27" };
        portfolio.Coe = new CoeAnalyticsVm();

        var result = await service.GetAsync();

        Assert.Null(result.Arpp);
        Assert.Null(result.Coe);
        Assert.True(result.HasAnything); // the slowest stages are still there
    }

    private static DashboardAnalyticsService Build(out StubPortfolio portfolio, out StubArpp arpp)
    {
        portfolio = new StubPortfolio();
        arpp = new StubArpp();
        return new DashboardAnalyticsService(
            new MemoryCache(new MemoryCacheOptions()),
            portfolio,
            new StubInsights(),
            arpp,
            NullLogger<DashboardAnalyticsService>.Instance);
    }

    private sealed class StubArpp : IArppAnalyticsService
    {
        public bool Fail { get; set; }

        public ArppAnalyticsVm Result { get; set; } = new()
        {
            FinancialYearStart = 2026,
            FinancialYearDisplay = "2026-27",
            IsCurrentFinancialYear = true,
            ListedCount = 38,
            ListedIpaValue = 1_966_500_000m,
            NewCount = 11,
            CommittedLiabilityCount = 22,
            CarryForwardCount = 5,
            DelistedCount = 1,
            CompletedCount = 4,
            CancelledCount = 1,
            SupplyOrderCount = 13,
            AonToSupplyOrderCount = 13,
            PreAonCount = 7
        };

        public Task<ArppAnalyticsVm> BuildAsync(int? requestedFinancialYearStart, DateOnly today, bool showToday = false, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("ARPP is down") : Task.FromResult(Result);
    }

    private sealed class StubPortfolio : IPortfolioAnalyticsService
    {
        public int CompletedCalls { get; private set; }

        public CoeAnalyticsVm Coe { get; set; } = new()
        {
            TotalCoeProjects = 18,
            OngoingCount = 15,
            CompletedCount = 2,
            SubcategoriesByLifecycle = new[]
            {
                new CoeSubcategoryLifecycleVm("CoE Mid Term", 8, 0, 1, 9),
                new CoeSubcategoryLifecycleVm("CoE Short Term", 4, 2, 0, 6),
                new CoeSubcategoryLifecycleVm("CoE Long Term", 2, 0, 0, 2),
                new CoeSubcategoryLifecycleVm("CoE (no sub-category)", 1, 0, 0, 1)
            }
        };

        public Task<OngoingAnalyticsVm> BuildOngoingAsync(IReadOnlyCollection<int> selectedParentCategoryIds, IReadOnlyList<int> orderedParentCategoryIds, DateOnly referenceDateIst, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CompletedAnalyticsVm> BuildCompletedAsync(DateOnly referenceDateIst, CancellationToken cancellationToken = default)
        {
            CompletedCalls++;
            return Task.FromResult(new CompletedAnalyticsVm
            {
                ByCategory = Array.Empty<AnalyticsCategoryCountPoint>(),
                ByTechnical = Array.Empty<AnalyticsCategoryCountPoint>(),
                PerYear = new[] { new CompletedPerYearPoint(2012, 4), new CompletedPerYearPoint(2021, 27), new CompletedPerYearPoint(2026, 7) },
                PerYearByParentCategory = Array.Empty<CompletedPerYearByParentCategoryPoint>(),
                YearBoard = Array.Empty<CompletedYearBoardItemVm>(),
                TotalCompletedProjects = 158,
                CompletedThisYear = 7,
                RecentYearlyAverage = new CompletedYearlyAverageVm(2021, 2025, 61, 12.2)
            });
        }

        public Task<CoeAnalyticsVm> BuildCoeAsync(DateOnly referenceDateIst, CancellationToken cancellationToken = default) =>
            Task.FromResult(Coe);

        public Task<int> CountCoeProjectsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Coe.TotalCoeProjects);
    }

    private sealed class StubInsights : IProjectAnalyticsService
    {
        public bool Empty { get; init; }

        private static readonly (string Name, double Median, int Projects)[] Stages =
        {
            ("Feasibility Study", 20, 30),
            ("SOW Vetting", 24, 30),
            ("Acceptance of Necessity", 36, 39),
            ("Bid Opening", 10, 12),
            ("Development", 75, 11),
            ("Transfer of Technology", 400, 0)
        };

        public Task<StageTimeInsightsVm> GetStageTimeInsightsAsync(int? categoryId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StageTimeInsightsVm
            {
                StageHotspots = Stages
                    .Where(_ => !Empty)
                    .Select((stage, index) => new StageHotspotPointVm
                    {
                        StageKey = stage.Name,
                        StageName = stage.Name,
                        StageOrder = index,
                        MedianDays = stage.Median,
                        AverageDays = stage.Median,
                        ProjectCount = stage.Projects
                    })
                    .ToList()
            });

        public Task<CategoryShareResult> GetCategoryShareAsync(ProjectLifecycleFilter lifecycle, int? categoryId = null, int? technicalCategoryId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StageDistributionResult> GetStageDistributionAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SlipBucketResult> GetSlipBucketsAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<int>> GetProjectIdsForSlipBucketAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, string bucketKey, CancellationToken cancellationToken = default, IReadOnlyCollection<int>? expandedCategoryIds = null) =>
            throw new NotSupportedException();

        public Task<TopOverdueProjectsResult> GetTopOverdueProjectsAsync(ProjectLifecycleFilter lifecycle, int? categoryId, int? technicalCategoryId, int take, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
