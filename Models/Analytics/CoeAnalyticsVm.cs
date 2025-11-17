using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Analytics;

// SECTION: CoE analytics view model
public sealed class CoeAnalyticsVm
{
    // SECTION: Chart datasets
    public IReadOnlyList<LabelValuePoint> ByStage { get; init; } = Array.Empty<LabelValuePoint>();

    public IReadOnlyList<LabelValuePoint> ByLifecycleStatus { get; init; } = Array.Empty<LabelValuePoint>();

    public IReadOnlyList<CoeSubcategoryLifecyclePoint> BySubcategoryLifecycle { get; init; } = Array.Empty<CoeSubcategoryLifecyclePoint>();
    // END SECTION

    // SECTION: Roadmap summary
    public CoeRoadmapVm Roadmap { get; init; } = new();
    // END SECTION
}
// END SECTION

// SECTION: Shared analytics points
public sealed record LabelValuePoint(string Label, int Value);
// END SECTION

// SECTION: CoE sub-category dataset
public sealed record CoeSubcategoryLifecyclePoint(string Subcategory, string LifecycleStatus, int Value);
// END SECTION

// SECTION: CoE roadmap view model
public sealed class CoeRoadmapVm
{
    public IReadOnlyList<string> ShortTerm { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MidTerm { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> LongTerm { get; init; } = Array.Empty<string>();
}
// END SECTION
