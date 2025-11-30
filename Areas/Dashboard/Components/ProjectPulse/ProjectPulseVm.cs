using System.Collections.Generic;

namespace ProjectManagement.Areas.Dashboard.Components.ProjectPulse;

// SECTION: Widget view model contract
public sealed class ProjectPulseVm
{
    // SECTION: Header
    public required int ProliferationEligible { get; init; }
    public required string AnalyticsUrl { get; init; }
    // END SECTION

    // SECTION: Counts
    public required int CompletedCount { get; init; }
    public required int OngoingCount { get; init; }
    public required int TotalOngoingProjects { get; init; }
    public required int TotalProjects { get; init; }
    // END SECTION

    // SECTION: Chart series
    public required IReadOnlyList<BarPoint> CompletedByYear { get; init; }
    public required IReadOnlyList<OngoingCategorySlice> OngoingByCategory { get; init; }
    public required IReadOnlyList<CategorySlice> AllByTechnicalCategoryTop { get; init; }
    public required int RemainingTechCategories { get; init; }
    // END SECTION

    // SECTION: Links
    public required string CompletedUrl { get; init; }
    public required string OngoingUrl { get; init; }
    public required string RepositoryUrl { get; init; }
    // END SECTION
}
// END SECTION

// SECTION: Category slice contract
public sealed record CategorySlice(string Label, int Count);
// END SECTION

// SECTION: Ongoing category slice contract
public sealed record OngoingCategorySlice(string CategoryName, int ProjectCount);
// END SECTION

// SECTION: Bar point contract
public sealed record BarPoint(string Label, int Count);
// END SECTION

