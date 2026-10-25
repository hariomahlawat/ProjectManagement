namespace ProjectManagement.Models.Analytics;

// SECTION: Completed analytics view model
public sealed class CompletedAnalyticsVm
{
    public required IReadOnlyList<AnalyticsCategoryCountPoint> ByCategory { get; init; }
    public required IReadOnlyList<AnalyticsCategoryCountPoint> ByTechnical { get; init; }
    public required IReadOnlyList<CompletedPerYearPoint> PerYear { get; init; }
    public required IReadOnlyList<CompletedPerYearByParentCategoryPoint> PerYearByParentCategory { get; init; }
    public required IReadOnlyList<CompletedYearBoardItemVm> YearBoard { get; init; }
    public int TotalCompletedProjects { get; init; }

    // SECTION: Headline figures
    public int CompletedThisYear { get; init; }
    public int? BestYear { get; init; }
    public int BestYearCount { get; init; }
    /// <summary>Median calendar days from the first stage start to completion; null when no project has both dates.</summary>
    public double? MedianDaysToComplete { get; init; }
    // END SECTION

    // SECTION: In-page drill-downs for the category and technical-category charts
    public IReadOnlyList<AnalyticsBoardGroupVm> CategoryBoard { get; init; } = Array.Empty<AnalyticsBoardGroupVm>();
    public IReadOnlyList<AnalyticsBoardGroupVm> TechnicalBoard { get; init; } = Array.Empty<AnalyticsBoardGroupVm>();
    // END SECTION
}

// SECTION: Completed year board DTOs
public sealed record CompletedYearBoardProjectVm(
    int ProjectId,
    string ProjectName,
    DateOnly? CompletedOn);

public sealed record CompletedYearBoardCategoryVm(
    string ParentCategoryName,
    int CategoryCount,
    IReadOnlyList<CompletedYearBoardProjectVm> Projects);

public sealed record CompletedYearBoardItemVm(
    int Year,
    int YearCount,
    IReadOnlyList<CompletedYearBoardCategoryVm> Categories);
// END SECTION

// SECTION: Shared analytics DTOs
/// <summary>Count for one exact category; <paramref name="Id"/> is null for projects without one. The drill-down key is the id, since names are only unique per parent.</summary>
public sealed record AnalyticsCategoryCountPoint(string Name, int Count, int? Id = null);

public sealed record CompletedPerYearPoint(int Year, int Count);

public sealed record CompletedPerYearByParentCategoryPoint(int Year, string CategoryName, int Count);

public sealed record OngoingStageByParentCategoryPoint(
    string StageCode,
    string StageName,
    string CategoryName,
    int Count);
// END SECTION
