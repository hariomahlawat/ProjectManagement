using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Where an active project stands, for the Projects register (docs/ux/projects-register.md).
/// Every fact comes from <see cref="OngoingProjectsReadService"/>, which the Ongoing page and the
/// Briefing desk also read: the current stage, its age (counted from the stage's recorded start
/// or, when none is recorded, from the latest completion, per the stage-start inference rule) and
/// the stage PDC. The register, its quick look and its export project these facts and never
/// recompute them.
/// </summary>
public sealed record ProjectRegisterActiveFacts(
    int ProjectId,
    IReadOnlyList<ProjectRegisterStageMark> Stages,
    int CurrentStageIndex,
    string CurrentStageCode,
    string CurrentStageName,
    ProjectRegisterStageState CurrentStageState,
    DateOnly? StageAgeFrom,
    int? DaysInStage,
    DateOnly? StagePdc,
    int? PlannedDays,
    int? DaysLate,
    int? DaysLeft,
    bool IsProlonged,
    bool IsApproachingProlonged,
    DateOnly LastUpdatedOn,
    int DaysSinceUpdate)
{
    /// <summary>A project not updated for longer than this needs attention.</summary>
    public const int StaleAfterDays = 30;

    public ProjectRegisterPlanStatus PlanStatus => StagePdc is null
        ? ProjectRegisterPlanStatus.NoPdc
        : DaysLate is > 0 ? ProjectRegisterPlanStatus.Late : ProjectRegisterPlanStatus.OnPlan;

    public bool IsBlocked => CurrentStageState == ProjectRegisterStageState.Blocked;

    public bool IsLate => PlanStatus == ProjectRegisterPlanStatus.Late;

    public bool IsStale => DaysSinceUpdate > StaleAfterDays;

    /// <summary>
    /// Projects one ongoing row. <paramref name="lastUpdatedOn"/> is the project's latest recorded
    /// activity (IST date); <paramref name="today"/> is the IST date the row was read for.
    /// </summary>
    public static ProjectRegisterActiveFacts From(OngoingProjectRowDto row, DateOnly lastUpdatedOn, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(row);

        var stages = row.Stages;
        var currentIndex = Math.Max(0, stages.ToList().FindIndex(stage => stage.IsCurrent));
        var current = stages.Count > 0 ? stages[currentIndex] : null;

        var state = current?.Status switch
        {
            StageStatus.InProgress => ProjectRegisterStageState.InProgress,
            StageStatus.Blocked => ProjectRegisterStageState.Blocked,
            _ => ProjectRegisterStageState.NotStarted
        };

        // Derived from the shared age so the two always agree, rather than re-deciding which
        // date a stage is counted from.
        DateOnly? ageFrom = row.DaysInCurrentStage is int days ? today.AddDays(-days) : null;
        var pdc = row.CurrentStagePdc;
        int? plannedDays = pdc is { } due && ageFrom is { } from && due >= from
            ? due.DayNumber - from.DayNumber
            : null;
        int? daysLeft = pdc is { } dueDate && dueDate >= today
            ? dueDate.DayNumber - today.DayNumber
            : null;

        var marks = stages
            .Select((stage, index) => new ProjectRegisterStageMark(
                stage.Code,
                stage.Name,
                MarkOf(stage.Status, index, currentIndex)))
            .ToList();

        return new ProjectRegisterActiveFacts(
            row.ProjectId,
            marks,
            currentIndex,
            row.CurrentStageCode,
            row.CurrentStageName ?? row.CurrentStageCode,
            state,
            ageFrom,
            row.DaysInCurrentStage,
            pdc,
            plannedDays,
            row.DaysPastPdc,
            daysLeft,
            row.IsCurrentStageProlonged,
            row.IsCurrentStageApproachingProlonged,
            lastUpdatedOn,
            Math.Max(0, today.DayNumber - lastUpdatedOn.DayNumber));
    }

    private static ProjectRegisterStageMarkState MarkOf(StageStatus status, int index, int currentIndex)
    {
        if (index == currentIndex)
        {
            return status switch
            {
                StageStatus.InProgress => ProjectRegisterStageMarkState.Current,
                StageStatus.Blocked => ProjectRegisterStageMarkState.Blocked,
                _ => ProjectRegisterStageMarkState.Waiting
            };
        }

        return status switch
        {
            StageStatus.Completed => ProjectRegisterStageMarkState.Done,
            StageStatus.Skipped => ProjectRegisterStageMarkState.Skipped,
            _ => ProjectRegisterStageMarkState.Upcoming
        };
    }
}

/// <summary>One workflow stage in the register's stage tracker.</summary>
public sealed record ProjectRegisterStageMark(string Code, string Name, ProjectRegisterStageMarkState State);

public enum ProjectRegisterStageMarkState
{
    Upcoming,
    Done,
    Skipped,
    Current,
    Blocked,
    Waiting
}

/// <summary>The current stage: under way, blocked, or not yet started (waiting on its start).</summary>
public enum ProjectRegisterStageState
{
    InProgress,
    Blocked,
    NotStarted
}

public enum ProjectRegisterPlanStatus
{
    NoPdc,
    OnPlan,
    Late
}
