using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.Dashboard.Components.ProjectPulse;

// SECTION: Widget view model
public sealed class ProjectPulseVm
{
    // SECTION: Header KPIs
    public int TotalProjects { get; init; }
    public int CompletedCount { get; init; }
    public int OngoingCount { get; init; }
    public int IdleCount { get; init; }
    public int AvailableForProliferationCount { get; init; }
    // END SECTION

    // SECTION: Chart data
    public IReadOnlyList<LabelValue> CompletedByProjectCategory { get; init; } = Array.Empty<LabelValue>();
    public IReadOnlyList<LabelValue> OngoingByStage { get; init; } = Array.Empty<LabelValue>();
    public IReadOnlyList<LabelValue> AllByTechnicalCategory { get; init; } = Array.Empty<LabelValue>();
    // END SECTION

    // SECTION: View flags
    public bool Condensed { get; init; }
    // END SECTION
}
// END SECTION

// SECTION: Shared chart label/value pair
public sealed record LabelValue(string Label, int Value);
// END SECTION
