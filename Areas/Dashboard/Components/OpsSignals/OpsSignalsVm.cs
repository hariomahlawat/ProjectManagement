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
    public string? Caption { get; init; }
    // END SECTION

    // SECTION: Sparkline data
    public IReadOnlyList<int>? Sparkline { get; init; }
    public IReadOnlyList<string>? SparklineLabels { get; init; }
    // END SECTION

    // SECTION: Delta indicators
    public int? DeltaAbs { get; init; }
    public double? DeltaPct { get; init; }
    // END SECTION

    // SECTION: Navigation + icon
    public required string LinkUrl { get; init; }
    public required string Icon { get; init; }
    // END SECTION
}
