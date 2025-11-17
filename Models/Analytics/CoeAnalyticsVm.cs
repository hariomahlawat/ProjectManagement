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

    // SECTION: Roadmap summary
    public CoeRoadmapVm Roadmap { get; init; } = new();
    // END SECTION

    // SECTION: Aggregate helpers
    public int TotalCoeProjects { get; init; }

    public bool HasCoeProjects => TotalCoeProjects > 0;

    public bool HasSubcategoryBreakdown => SubcategoriesByLifecycle.Count > 0;
    // END SECTION
}
// END SECTION

// SECTION: CoE stage dataset
public sealed record CoeStageBucketVm(string StageName, int ProjectCount);
// END SECTION

// SECTION: CoE sub-category dataset
public sealed record CoeSubcategoryLifecycleVm(
    string Name,
    int Ongoing,
    int Completed,
    int Cancelled,
    int Total);
// END SECTION

// SECTION: CoE roadmap view model
public sealed class CoeRoadmapVm
{
    public IReadOnlyList<string> ShortTerm { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MidTerm { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> LongTerm { get; init; } = Array.Empty<string>();
}
// END SECTION
