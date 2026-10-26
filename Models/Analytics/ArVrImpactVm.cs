using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Analytics;

// SECTION: AR/VR proliferation and training (CoE (AR/VR) mode)
/// <summary>
/// What the AR/VR projects have delivered beyond the workflow: builds proliferated to units and
/// people trained. Scope is every AR/VR project (technical category), in or out of a CoE category.
/// </summary>
public sealed class ArVrImpactVm
{
    // SECTION: Proliferation (approved rows only, calendar years, the tracker's year-preference rule per project, source and year)
    public int ProliferationUnitsDelivered { get; init; }
    /// <summary>Distinct receiving units named in the approved granular ledger.</summary>
    public int ProliferationReceivingUnits { get; init; }
    public int ProliferationProjects { get; init; }
    /// <summary>Units delivered in the calendar year of the "as of" date.</summary>
    public int ProliferationThisYear { get; init; }
    public int ProliferationCurrentYear { get; init; }
    public IReadOnlyList<ArVrProliferationYearVm> ProliferationByYear { get; init; } = Array.Empty<ArVrProliferationYearVm>();
    /// <summary>One row per year (latest first): units delivered, then the projects with their units and receiving units.</summary>
    public IReadOnlyList<AnalyticsBoardGroupVm> ProliferationBoard { get; init; } = Array.Empty<AnalyticsBoardGroupVm>();
    public bool HasProliferation => ProliferationByYear.Count > 0;
    // END SECTION

    // SECTION: Training (training years April to March, as the training tracker counts; each course counted once)
    public int TrainingCourses { get; init; }
    public int TrainingPeople { get; init; }
    public int TrainingProjects { get; init; }
    /// <summary>People trained in the training year of the "as of" date.</summary>
    public int TrainingPeopleThisYear { get; init; }
    public string TrainingCurrentYearLabel { get; init; } = string.Empty;
    public IReadOnlyList<ArVrTrainingYearVm> TrainingByYear { get; init; } = Array.Empty<ArVrTrainingYearVm>();
    /// <summary>One row per training year (latest first): people trained, then the projects with their courses and people. A course linked to several projects appears under each, so the project figures can add up to more than the row.</summary>
    public IReadOnlyList<AnalyticsBoardGroupVm> TrainingBoard { get; init; } = Array.Empty<AnalyticsBoardGroupVm>();
    public bool HasTraining => TrainingByYear.Count > 0;
    // END SECTION

    public bool HasData => HasProliferation || HasTraining;
}

/// <summary>Units delivered in one calendar year, by source (SDD and ABW 515).</summary>
public sealed record ArVrProliferationYearVm(int Year, int Sdd, int Abw515, int Projects, int ReceivingUnits)
{
    public int Total => Sdd + Abw515;
}

/// <summary>People trained in one training year (April to March), by rank group.</summary>
public sealed record ArVrTrainingYearVm(int StartYear, string Label, int Courses, int Officers, int Jcos, int OtherRanks, int Projects)
{
    public int People => Officers + Jcos + OtherRanks;
}
// END SECTION
