namespace ProjectManagement.Models.Analytics;

// SECTION: Completed analytics view model
public sealed class CompletedAnalyticsVm
{
    public required IReadOnlyList<AnalyticsCategoryCountPoint> ByCategory { get; init; }
    public required IReadOnlyList<AnalyticsCategoryCountPoint> ByTechnical { get; init; }
    public required IReadOnlyList<CompletedPerYearPoint> PerYear { get; init; }
    public required IReadOnlyList<CompletedPerYearByParentCategoryPoint> PerYearByParentCategory { get; init; }
    public required IReadOnlyList<CompletedYearBoardItemVm> YearBoard { get; init; }
    /// <summary>The "5-year average" view: the current year on its own, then five-year bands counting back from it, oldest first.</summary>
    public IReadOnlyList<CompletedYearBandVm> YearBands { get; init; } = Array.Empty<CompletedYearBandVm>();
    public int TotalCompletedProjects { get; init; }

    // SECTION: Headline figures
    public int CompletedThisYear { get; init; }
    /// <summary>Completions a year over the last five complete calendar years (fewer when the history is shorter); null before the first complete year.</summary>
    public CompletedYearlyAverageVm? RecentYearlyAverage { get; init; }
    /// <summary>The same figure for the five complete years before that window; null when nothing was completed that early.</summary>
    public CompletedYearlyAverageVm? PreviousYearlyAverage { get; init; }
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

// SECTION: Five-year band DTOs
/// <summary>
/// Completions in a band of calendar years. A full band is five years and its average divides by five,
/// whether or not every year had a completion; the current-year band is that one (partial) year.
/// </summary>
public sealed record CompletedYearBandVm(
    int FromYear,
    int ToYear,
    bool IsCurrentYear,
    int Count,
    double PerYear,
    IReadOnlyList<CompletedYearBandCategoryVm> Categories)
{
    public int Years => ToYear - FromYear + 1;

    /// <summary>"2021–2025", or "2026" for a one-year band.</summary>
    public string Label => FromYear == ToYear ? FromYear.ToString() : $"{FromYear}–{ToYear}";

    /// <summary>The axis label: "2021–25", or "1996–2000" when the band crosses a century.</summary>
    public string ShortLabel => FromYear == ToYear
        ? FromYear.ToString()
        : FromYear / 100 == ToYear / 100 ? $"{FromYear}–{ToYear % 100:00}" : $"{FromYear}–{ToYear}";
}

public sealed record CompletedYearBandCategoryVm(
    string ParentCategoryName,
    int CategoryCount,
    double PerYear,
    IReadOnlyList<CompletedYearBoardProjectVm> Projects);
// END SECTION

// SECTION: Shared analytics DTOs
/// <summary>Count for one exact category; <paramref name="Id"/> is null for projects without one. The drill-down key is the id, since names are only unique per parent.</summary>
public sealed record AnalyticsCategoryCountPoint(string Name, int Count, int? Id = null);

public sealed record CompletedPerYearPoint(int Year, int Count);

/// <summary>Average completions a year over a window of complete calendar years; a year with no completions counts as zero.</summary>
public sealed record CompletedYearlyAverageVm(int FromYear, int ToYear, int Count, double PerYear)
{
    public int Years => ToYear - FromYear + 1;
}

public sealed record CompletedPerYearByParentCategoryPoint(int Year, string CategoryName, int Count);

public sealed record OngoingStageByParentCategoryPoint(
    string StageCode,
    string StageName,
    string CategoryName,
    int Count);
// END SECTION
