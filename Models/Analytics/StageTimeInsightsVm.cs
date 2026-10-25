using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Analytics;

// SECTION: Stage time insight view models
public static class StageTimeBucketKeys
{
    public const string BelowOneCrore = "Below1Cr";
    public const string AboveOrEqualOneCrore = "AboveOrEqual1Cr";
}

public sealed class StageTimeInsightsVm
{
    public IReadOnlyList<StageTimeBucketRowVm> Rows { get; init; } = Array.Empty<StageTimeBucketRowVm>();

    public IReadOnlyList<StageHotspotPointVm> StageHotspots { get; init; } = Array.Empty<StageHotspotPointVm>();

    public bool HasData => Rows.Count > 0;

    public int? SelectedCategoryId { get; init; }
}

// SECTION: Project management insights panel view models
public sealed class StageTimeInsightsPanelVm
{
    public StageTimeCycleChartVm StageCycleTime { get; init; } = new();

    public StageHotspotChartVm StageHotspots { get; init; } = new();

    // SECTION: Ranked hotspots and portfolio baseline (computed once in the PageModel)
    /// <summary>Category filter shared by the cycle-time chart and the hotspot list.</summary>
    public int? SelectedCategoryId { get; init; }

    /// <summary>Median of the per-stage medians: the baseline a stage is compared against.</summary>
    public double PortfolioMedianDays { get; init; }

    /// <summary>Stages ranked slowest first, with their ratio to the portfolio median.</summary>
    public IReadOnlyList<StageHotspotRankVm> RankedHotspots { get; init; } = Array.Empty<StageHotspotRankVm>();

    public StageHotspotRankVm? SlowestStage => RankedHotspots.Count > 0 ? RankedHotspots[0] : null;
    public StageHotspotRankVm? FastestStage => RankedHotspots.Count > 0 ? RankedHotspots[^1] : null;
    // END SECTION
}

public sealed record StageHotspotRankVm(
    int Rank,
    string StageKey,
    string StageName,
    double MedianDays,
    double AverageDays,
    int ProjectCount,
    double RatioToPortfolioMedian,
    double ShareOfSlowest);

public sealed class StageTimeCycleChartVm
{
    public IReadOnlyList<StageTimeBucketRowVm> Rows { get; init; } = Array.Empty<StageTimeBucketRowVm>();

    public bool HasData => Rows.Count > 0;

    public int? SelectedCategoryId { get; init; }
}

public sealed class StageHotspotChartVm
{
    public IReadOnlyList<StageHotspotPointVm> Points { get; init; } = Array.Empty<StageHotspotPointVm>();

    public bool HasData => Points.Count > 0;

    public int? SelectedCategoryId { get; init; }
}
// END SECTION

public sealed class StageTimeBucketRowVm
{
    public string StageKey { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public int StageOrder { get; init; }

    public string Bucket { get; init; } = string.Empty;

    public double MedianDays { get; init; }
    public double AverageDays { get; init; }

    public int ProjectCount { get; init; }
    public int CompletedProjectCount { get; init; }
    public int OngoingProjectCount { get; init; }
}

public sealed class StageHotspotPointVm
{
    public string StageKey { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public int StageOrder { get; init; }
    public double MedianDays { get; init; }
    public double AverageDays { get; init; }
    public int ProjectCount { get; init; }
}
// END SECTION
