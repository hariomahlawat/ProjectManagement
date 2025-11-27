using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Analytics;

// SECTION: CoE analytics view model
public sealed class CoeAnalyticsVm
{
    // SECTION: Chart datasets
    public IReadOnlyList<CoeStageBucketVm> ByStage { get; init; } = Array.Empty<CoeStageBucketVm>();

    public IReadOnlyList<CoeSubcategoryLifecycleVm> SubcategoriesByLifecycle { get; init; } = Array.Empty<CoeSubcategoryLifecycleVm>();
    // END SECTION

    // SECTION: Aggregate helpers
    public int TotalCoeProjects { get; init; }

    public bool HasCoeProjects => TotalCoeProjects > 0;

    public bool HasSubcategoryBreakdown => SubcategoriesByLifecycle.Count > 0;
    // END SECTION

    // SECTION: Sub-category project listings
    public IReadOnlyList<CoeSubcategoryProjectsVm> SubcategoryProjects { get; init; } =
        Array.Empty<CoeSubcategoryProjectsVm>();
    // END SECTION
}
// END SECTION

// SECTION: CoE stage dataset
public sealed record CoeStageBucketVm(
    string StageKey,
    string StageName,
    int ProjectCount);
// END SECTION

// SECTION: CoE sub-category dataset
public sealed record CoeSubcategoryLifecycleVm(
    string Name,
    int Ongoing,
    int Completed,
    int Cancelled,
    int Total);
// END SECTION

// SECTION: CoE sub-category project listing dataset
public sealed record CoeSubcategoryProjectsVm(
    string SubcategoryName,
    IReadOnlyList<CoeProjectSummaryVm> Projects);

public sealed record CoeProjectSummaryVm(
    int Id,
    string Name,
    string LifecycleStatus,
    string CurrentStage);
// END SECTION
