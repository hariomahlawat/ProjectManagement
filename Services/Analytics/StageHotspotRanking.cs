using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Analytics;

/// <summary>
/// The slowest-stage ranking of Analytics → Insights, built once so the Insights headlines and the
/// dashboard's Analytics card name the same stages with the same figures.
/// </summary>
public static class StageHotspotRanking
{
    /// <summary>
    /// Measured stages, slowest median first (workflow order breaks a tie), each read against the
    /// portfolio median: the median of the stage medians.
    /// </summary>
    public static IReadOnlyList<StageHotspotRankVm> Rank(
        IReadOnlyList<StageHotspotPointVm> points,
        out double portfolioMedian)
    {
        var measured = points
            .Where(point => point.ProjectCount > 0)
            .OrderByDescending(point => point.MedianDays)
            .ThenBy(point => point.StageOrder)
            .ToList();

        portfolioMedian = AnalyticsAggregation.CalculateMedian(measured.Select(point => point.MedianDays));
        var slowestMedian = measured.Count > 0 ? measured[0].MedianDays : 0;
        var baseline = portfolioMedian;

        return measured
            .Select((point, index) => new StageHotspotRankVm(
                Rank: index + 1,
                StageKey: point.StageKey,
                StageName: point.StageName,
                MedianDays: point.MedianDays,
                AverageDays: point.AverageDays,
                ProjectCount: point.ProjectCount,
                RatioToPortfolioMedian: baseline > 0 ? point.MedianDays / baseline : 0,
                ShareOfSlowest: slowestMedian > 0 ? point.MedianDays / slowestMedian : 0))
            .ToList();
    }
}
