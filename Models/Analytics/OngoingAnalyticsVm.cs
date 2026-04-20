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

    public IReadOnlyList<OngoingStageBoardItemVm> StageBoard { get; init; } =
        Array.Empty<OngoingStageBoardItemVm>();

    public IReadOnlyList<AnalyticsStageDurationPoint> AvgStageDurations { get; init; } =
        Array.Empty<AnalyticsStageDurationPoint>();
}

public sealed record OngoingStageBoardItemVm(
    string StageCode,
    string StageName,
    int StageCount,
    IReadOnlyList<OngoingStageBoardCategoryVm> Categories);

public sealed record OngoingStageBoardCategoryVm(
    int? ParentCategoryId,
    string ParentCategoryName,
    int CategoryCount,
    IReadOnlyList<OngoingStageBoardProjectVm> Projects);

public sealed record OngoingStageBoardProjectVm(int ProjectId, string ProjectName);
// END SECTION
