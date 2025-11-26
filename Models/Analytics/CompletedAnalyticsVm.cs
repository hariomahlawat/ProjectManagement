namespace ProjectManagement.Models.Analytics;

// SECTION: Completed analytics view model
public sealed class CompletedAnalyticsVm
{
    public required IReadOnlyList<AnalyticsCategoryCountPoint> ByCategory { get; init; }
    public required IReadOnlyList<AnalyticsCategoryCountPoint> ByTechnical { get; init; }
    public required IReadOnlyList<CompletedPerYearPoint> PerYear { get; init; }
    public required IReadOnlyList<CompletedPerYearByParentCategoryPoint> PerYearByParentCategory { get; init; }
    public int TotalCompletedProjects { get; init; }
}

// SECTION: Shared analytics DTOs
public sealed record AnalyticsCategoryCountPoint(string Name, int Count);

public sealed record AnalyticsStageCountPoint(string Name, int Count);

public sealed record AnalyticsStageDurationPoint(string StageCode, string Name, double Days);

public sealed record CompletedPerYearPoint(int Year, int Count);

public sealed record CompletedPerYearByParentCategoryPoint(int Year, string CategoryName, int Count);
// END SECTION
