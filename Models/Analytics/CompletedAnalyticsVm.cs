namespace ProjectManagement.Models.Analytics;

// SECTION: Completed analytics view model
public sealed class CompletedAnalyticsVm
{
    public required IReadOnlyList<CompletedByCategoryPoint> ByCategory { get; init; }
    public required IReadOnlyList<CompletedByTechnicalPoint> ByTechnical { get; init; }
    public required IReadOnlyList<CompletedPerYearPoint> PerYear { get; init; }
    public int TotalCompletedProjects { get; init; }
}

public sealed record CompletedByCategoryPoint(string CategoryName, int Count);

public sealed record CompletedByTechnicalPoint(string TechnicalCategoryName, int Count);

public sealed record CompletedPerYearPoint(int Year, int Count);
// END SECTION
