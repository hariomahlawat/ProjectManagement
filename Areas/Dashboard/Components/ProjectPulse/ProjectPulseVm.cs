using System.Collections.Generic;
using ProjectManagement.Services.Analytics;

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
    public required int CompletedUniqueCount { get; init; }
    public required int CompletedRebuildCount { get; init; }
    public required int OngoingCount { get; init; }
    public required int TotalProjects { get; init; }
    // END SECTION

    // SECTION: Chart series
    public required IReadOnlyList<CategorySlice> OngoingByProjectCategory { get; init; }
    public required StageDistributionResult OngoingStageDistributionTotal { get; init; }
    public required IReadOnlyList<OngoingStageDistributionCategoryVm> OngoingStageDistributionByCategory { get; init; }
    public required IReadOnlyDictionary<string, OngoingBucketSetVm> OngoingBucketsByKey { get; init; }
    public required IReadOnlyList<OngoingBucketFilterVm> OngoingBucketFilters { get; init; }
    public required IReadOnlyList<CategorySlice> AllByTechnicalCategoryTop { get; init; }
    public required int RemainingTechCategories { get; init; }
    public required IReadOnlyList<TreemapNode> UniqueCompletedByTechnicalCategory { get; init; }
    public required IReadOnlyList<TreemapNode> UniqueCompletedByProjectType { get; init; }
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

// SECTION: Ongoing stage distribution contract
public sealed record OngoingStageDistributionCategoryVm(
    int ParentCategoryId,
    string Label,
    StageDistributionResult StageDistribution);
// END SECTION

// SECTION: Ongoing bucket filter contract
public sealed record OngoingBucketFilterVm(string Key, string Label);
// END SECTION

// SECTION: Ongoing bucket set contract
public sealed record OngoingBucketSetVm(
    int Total,
    int Apvl,
    int Aon,
    int Tender,
    int Devp,
    int Other);
// END SECTION

// SECTION: Treemap node contract
public sealed record TreemapNode(string Label, int Count, string? Url);
// END SECTION
