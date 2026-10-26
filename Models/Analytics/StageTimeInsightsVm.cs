using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Analytics;

// SECTION: Stage time insight view models
/// <summary>The two cost bands of the Insights chart: up to and including ₹2 crore, and above it.</summary>
public static class StageTimeBucketKeys
{
    public const string UpToTwoCrore = "UpTo2Cr";
    public const string AboveTwoCrore = "Above2Cr";
}

public sealed class StageTimeInsightsVm
{
    /// <summary>One row per stage and cost band.</summary>
    public IReadOnlyList<StageTimeBucketRowVm> Rows { get; init; } = Array.Empty<StageTimeBucketRowVm>();

    /// <summary>One row per stage and parent (root) category; <see cref="StageTimeBucketRowVm.Bucket"/> is the root category name.</summary>
    public IReadOnlyList<StageTimeBucketRowVm> CategoryRows { get; init; } = Array.Empty<StageTimeBucketRowVm>();

    public IReadOnlyList<StageHotspotPointVm> StageHotspots { get; init; } = Array.Empty<StageHotspotPointVm>();

    public bool HasData => Rows.Count > 0;

    public int? SelectedCategoryId { get; init; }

    /// <summary>Finished stages measured, one per project and stage.</summary>
    public int MeasuredStageCount { get; init; }

    /// <summary>Of <see cref="MeasuredStageCount"/>, the stages measured from an inferred start.</summary>
    public int InferredStartStageCount { get; init; }
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

    /// <summary>Finished stages measured, and how many of them from an inferred start (the footnote's disclosure).</summary>
    public int MeasuredStageCount { get; init; }

    public int InferredStartStageCount { get; init; }

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

    public IReadOnlyList<StageTimeBucketRowVm> CategoryRows { get; init; } = Array.Empty<StageTimeBucketRowVm>();

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
