using System.Collections.Generic;

namespace ProjectManagement.Areas.Dashboard.Components.OpsSignals;

public sealed class OpsSignalsVm
{
    // SECTION: Tile summary
    public required IReadOnlyList<OpsTileVm> Tiles { get; init; }
    public DateOnly? RangeStart { get; init; }
    public DateOnly? RangeEnd { get; init; }
    // END SECTION
}

public sealed class OpsTileVm
{
    // SECTION: Tile identity
    public required string Key { get; init; }
    public required string Label { get; init; }
    // END SECTION

    // SECTION: Primary values
    public required long Value { get; init; }
    public string? Unit { get; init; }
    public long? SecondaryValue { get; init; }
    public string? SecondaryLabel { get; init; }
    public string? Caption { get; init; }
    public string? TrendLabel { get; init; }
    // END SECTION

    // SECTION: Sparkline data
    public IReadOnlyList<int>? Sparkline { get; init; }
    public IReadOnlyList<string>? SparklineLabels { get; init; }
    // END SECTION

    // SECTION: Delta indicators
    public int? DeltaAbs { get; init; }
    public double? DeltaPct { get; init; }
    public string? DeltaLabel { get; init; }
    /// <summary>What the change compares, shown beside it: "vs previous 3 months".</summary>
    public string? DeltaBasis { get; init; }
    /// <summary>The exact months compared, for a tooltip: "Aug–Oct 2026 compared with May–Jul 2026".</summary>
    public string? DeltaPeriods { get; init; }
    // END SECTION

    // SECTION: Navigation + icon
    public required string LinkUrl { get; init; }
    public required string Icon { get; init; }
    // END SECTION
}
