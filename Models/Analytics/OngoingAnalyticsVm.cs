namespace ProjectManagement.Models.Analytics;

// SECTION: Ongoing analytics view model
public sealed class OngoingAnalyticsVm
{
    public int TotalOngoingProjects { get; init; }

    public IReadOnlyList<AnalyticsCategoryCountPoint> ByCategory { get; init; } =
        Array.Empty<AnalyticsCategoryCountPoint>();

    public IReadOnlyList<AnalyticsStageCountPoint> ByStage { get; init; } =
        Array.Empty<AnalyticsStageCountPoint>();

    public IReadOnlyList<OngoingStageByParentCategoryPoint> ByStageByParentCategory { get; init; } =
        Array.Empty<OngoingStageByParentCategoryPoint>();

    public IReadOnlyList<AnalyticsStageDurationPoint> AvgStageDurations { get; init; } =
        Array.Empty<AnalyticsStageDurationPoint>();
}
// END SECTION
