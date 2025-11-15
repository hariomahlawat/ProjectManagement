using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.Dashboard.Components.ProjectPulse;

// SECTION: Widget view model
public sealed class ProjectPulseVm
{
    // SECTION: KPI tiles
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Ongoing { get; init; }
    public int Idle { get; init; }
    // END SECTION

    // SECTION: Trend micro-charts
    public IReadOnlyList<int> CompletedByMonth { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> OngoingByMonth { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> NewByMonth { get; init; } = Array.Empty<int>();
    // END SECTION

    // SECTION: Deep links
    public string RepositoryUrl { get; init; } = "/Projects";
    public string CompletedUrl { get; init; } = "/Projects?status=Completed";
    public string OngoingUrl { get; init; } = "/Projects?status=Ongoing";
    public string AnalyticsUrl { get; init; } = "/Reports/Projects/Analytics";
    // END SECTION
}
