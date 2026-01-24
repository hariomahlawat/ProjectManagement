namespace ProjectManagement.Models.Analytics;

// SECTION: Ongoing analytics view model
public sealed class OngoingAnalyticsVm
{
    public int TotalOngoingProjects { get; init; }

    public IReadOnlyList<AnalyticsCategoryCountPoint> ByCategory { get; init; } =
        Array.Empty<AnalyticsCategoryCountPoint>();

    public IReadOnlyList<AnalyticsStageCountPoint> ByStage { get; init; } =
        Array.Empty<AnalyticsStageCountPoint>();

    public IReadOnlyList<AnalyticsStageDurationPoint> AvgStageDurations { get; init; } =
        Array.Empty<AnalyticsStageDurationPoint>();

    public IReadOnlyList<OngoingStageCountByCategoryPoint> ByStageByCategory { get; init; } =
        Array.Empty<OngoingStageCountByCategoryPoint>();

    public IReadOnlyList<OngoingStageDurationByCategoryPoint> AvgStageDurationsByCategory { get; init; } =
        Array.Empty<OngoingStageDurationByCategoryPoint>();
}
// END SECTION
