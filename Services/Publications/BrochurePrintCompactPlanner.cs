namespace ProjectManagement.Services.Publications;

/// <summary>
/// Compatibility surface retained for code that referenced Phase 8 compact-print constants.
/// Phase 9 planning is performed by <see cref="IBrochurePrintPagePlanner"/> using font-aware
/// measurements; no page membership should be decided through this type.
/// </summary>
[Obsolete("Use IBrochurePrintPagePlanner. Phase 9 replaced heuristic compact-print planning with measured composition.")]
public static class BrochurePrintCompactPlanner
{
    public static float ContentCapacityPoints
        => BrochurePrintLayoutMetrics.ProjectContentCapacity(hasHandlingMarking: false);

    public const float InterModuleSpacingPoints = BrochurePrintLayoutMetrics.InterModuleSpacingPoints;
    public const float ClosingGapPoints = BrochurePrintLayoutMetrics.ClosingGapPoints;
}
