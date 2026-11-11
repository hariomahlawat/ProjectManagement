using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Dashboard;

/// <summary>
/// The dashboard's Analytics card: a few figures from each Analytics mode, read from the same services
/// the Analytics page uses so the card never says something the page does not. The result is the same
/// for every user and is cached; the ARPP part is shown only to a caller the ARPP view policy admits
/// (the page decides that, per request).
/// </summary>
public interface IDashboardAnalyticsService
{
    Task<DashboardAnalyticsVm> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Each part is null (or empty) when it has nothing to show or could not be read.</summary>
public sealed record DashboardAnalyticsVm(
    DashboardArppVm? Arpp,
    DashboardCoeVm? Coe,
    DashboardCompletedVm? Completed,
    IReadOnlyList<DashboardSlowStageVm> SlowestStages)
{
    /// <summary>False when the Analytics card has nothing of its own to show (Completed has its own card).</summary>
    public bool HasAnything => Arpp is not null || Coe is not null || SlowestStages.Count > 0;
}

/// <summary>
/// The financial year the ARPP mode opens on, with the same standing split as its headline cards.
/// <paramref name="AsOfDate"/> is set when an earlier year is shown as it stood on its 31 March.
/// </summary>
public sealed record DashboardArppVm(
    int FinancialYearStart,
    string FinancialYearDisplay,
    DateOnly? AsOfDate,
    int ListedCount,
    string ListedIpaDisplay,
    int CompletedCount,
    int SupplyOrderCount,
    int AonToSupplyOrderCount,
    int PreAonCount,
    int NewCount,
    int CommittedLiabilityCount,
    int CarryForwardCount,
    int CancelledCount,
    int DelistedCount,
    int NotKnownCount);

public sealed record DashboardCoeVm(
    int Total,
    int Ongoing,
    int Completed,
    int Cancelled,
    IReadOnlyList<DashboardCoeSubcategoryVm> Subcategories,
    bool HasMoreSubcategories);

public sealed record DashboardCoeSubcategoryVm(string Name, int Ongoing, int Completed, int Cancelled, int Total);

/// <param name="LastYears">The ten years ending with <paramref name="Year"/>, oldest first; a year with no completion counts zero.</param>
public sealed record DashboardCompletedVm(
    int Total,
    int ThisYear,
    int Year,
    CompletedYearlyAverageVm? RecentAverage,
    IReadOnlyList<CompletedPerYearPoint> LastYears);

public sealed record DashboardSlowStageVm(string StageName, int MedianDays, double RatioToPortfolioMedian);

public sealed class DashboardAnalyticsService : IDashboardAnalyticsService
{
    private const string CacheKeyPrefix = "Dashboard:Analytics:";
    private const int SlowestStageCount = 3;
    private const int MaxCoeSubcategories = 3;
    private const int CompletedYearsShown = 10;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;
    private readonly IPortfolioAnalyticsService _portfolio;
    private readonly IProjectAnalyticsService _projectAnalytics;
    private readonly IArppAnalyticsService _arpp;
    private readonly ILogger<DashboardAnalyticsService> _logger;

    public DashboardAnalyticsService(
        IMemoryCache cache,
        IPortfolioAnalyticsService portfolio,
        IProjectAnalyticsService projectAnalytics,
        IArppAnalyticsService arpp,
        ILogger<DashboardAnalyticsService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        _projectAnalytics = projectAnalytics ?? throw new ArgumentNullException(nameof(projectAnalytics));
        _arpp = arpp ?? throw new ArgumentNullException(nameof(arpp));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DashboardAnalyticsVm> GetAsync(CancellationToken cancellationToken = default)
    {
        var todayIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstClock.TimeZone).DateTime);
        var cacheKey = CacheKeyPrefix + todayIst.ToString("yyyy-MM-dd");
        if (_cache.TryGetValue(cacheKey, out DashboardAnalyticsVm? cached) && cached is not null)
        {
            return cached;
        }

        var (summary, complete) = await BuildAsync(todayIst, cancellationToken);
        // A part that failed is not kept: the next load tries it again.
        if (complete)
        {
            _cache.Set(cacheKey, summary, CacheDuration);
        }

        return summary;
    }

    // The services share one DbContext, so the parts are read one after another.
    internal async Task<(DashboardAnalyticsVm Summary, bool Complete)> BuildAsync(DateOnly todayIst, CancellationToken cancellationToken)
    {
        var complete = true;

        async Task<T?> ReadAsync<T>(string part, Func<Task<T?>> read) where T : class
        {
            try
            {
                return await read();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                complete = false;
                _logger.LogError(ex, "Dashboard analytics card: {Part} could not be read", part);
                return null;
            }
        }

        var arpp = await ReadAsync("ARPP", async () => MapArpp(await _arpp.BuildAsync(null, todayIst, cancellationToken: cancellationToken)));
        var coe = await ReadAsync("CoE", async () => MapCoe(await _portfolio.BuildCoeAsync(todayIst, cancellationToken)));
        var completed = await ReadAsync("Completed", async () => MapCompleted(await _portfolio.BuildCompletedAsync(todayIst, cancellationToken), todayIst.Year));
        var slowest = await ReadAsync("Insights", async () => MapSlowest(await _projectAnalytics.GetStageTimeInsightsAsync(null, cancellationToken)));

        return (new DashboardAnalyticsVm(arpp, coe, completed, slowest ?? Array.Empty<DashboardSlowStageVm>()), complete);
    }

    private static DashboardArppVm? MapArpp(ArppAnalyticsVm arpp) =>
        !arpp.HasData
            ? null
            : new DashboardArppVm(
                arpp.FinancialYearStart,
                arpp.FinancialYearDisplay,
                arpp.AsOfDate,
                arpp.ListedCount,
                IndianCurrencyFormatter.FormatCompact(arpp.ListedIpaValue),
                arpp.CompletedCount,
                arpp.SupplyOrderCount,
                arpp.AonToSupplyOrderCount,
                arpp.PreAonCount,
                arpp.NewCount,
                arpp.CommittedLiabilityCount,
                arpp.CarryForwardCount,
                arpp.CancelledCount,
                arpp.DelistedCount,
                arpp.CardNotKnownCount);

    private static DashboardCoeVm? MapCoe(CoeAnalyticsVm coe)
    {
        if (!coe.HasCoeProjects)
        {
            return null;
        }

        var rows = coe.SubcategoriesByLifecycle;
        return new DashboardCoeVm(
            coe.TotalCoeProjects,
            coe.OngoingCount,
            coe.CompletedCount,
            rows.Sum(row => row.Cancelled),
            rows.Take(MaxCoeSubcategories)
                .Select(row => new DashboardCoeSubcategoryVm(row.Name, row.Ongoing, row.Completed, row.Cancelled, row.Total))
                .ToList(),
            rows.Count > MaxCoeSubcategories);
    }

    private static DashboardCompletedVm? MapCompleted(CompletedAnalyticsVm completed, int year) =>
        new(
            completed.TotalCompletedProjects,
            completed.CompletedThisYear,
            year,
            completed.RecentYearlyAverage,
            Enumerable.Range(year - (CompletedYearsShown - 1), CompletedYearsShown)
                .Select(y => new CompletedPerYearPoint(y, completed.PerYear.FirstOrDefault(point => point.Year == y)?.Count ?? 0))
                .ToList());

    private static IReadOnlyList<DashboardSlowStageVm>? MapSlowest(StageTimeInsightsVm insights) =>
        StageHotspotRanking.Rank(insights.StageHotspots, out _)
            .Take(SlowestStageCount)
            .Select(stage => new DashboardSlowStageVm(stage.StageName, (int)Math.Round(stage.MedianDays), stage.RatioToPortfolioMedian))
            .ToList();
}
