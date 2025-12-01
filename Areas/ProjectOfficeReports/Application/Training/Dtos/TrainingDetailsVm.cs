using System;
using System.Collections.Generic;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;

// -----------------------------------------------------------------------------
// TRAINING DETAILS VIEW MODEL
// -----------------------------------------------------------------------------
public sealed class TrainingDetailsVm
{
    public Guid Id { get; init; }

    public string TrainingTypeName { get; init; } = string.Empty;

    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string PeriodDisplay { get; init; } = string.Empty;
    public string PeriodDayCountDisplay { get; init; } = string.Empty;

    public string SourceDisplay { get; init; } = string.Empty;
    public int TotalTrainees { get; init; }
    public string StrengthDisplay { get; init; } = string.Empty;

    // ------------------------------------------------------------
    // roster summary
    // ------------------------------------------------------------
    public int OfficersCount { get; init; }
    public int JcosCount { get; init; }
    public int OrsCount { get; init; }
    public int TotalCount => OfficersCount + JcosCount + OrsCount;

    public string RosterSourceDisplay { get; init; } = string.Empty;

    public IReadOnlyList<TrainingTraineeVm> Roster { get; init; } = Array.Empty<TrainingTraineeVm>();

    // ------------------------------------------------------------
    // metadata / notes
    // ------------------------------------------------------------
    public IReadOnlyList<string> ProjectNames { get; init; } = Array.Empty<string>();
    public string Notes { get; init; } = string.Empty;

    public string CreatedByDisplayName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public string? LastModifiedByDisplayName { get; init; }
    public DateTimeOffset? LastModifiedAt { get; init; }
}

// -----------------------------------------------------------------------------
// TRAINING TRAINEE VIEW MODEL
// -----------------------------------------------------------------------------
public sealed class TrainingTraineeVm
{
    public string ArmyNumber { get; init; } = string.Empty;

    public string Rank { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string UnitName { get; init; } = string.Empty;

    public string CategoryLabel { get; init; } = string.Empty;
}
