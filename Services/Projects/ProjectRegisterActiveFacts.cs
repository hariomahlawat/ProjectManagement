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
    /// The register's default order: blocked first, then late (most overdue first), then on plan
    /// (nearest PDC first), then no PDC (longest in stage first), then finished. Callers break
    /// remaining ties by project name and id.
    /// </summary>
    public static IComparer<ProjectRegisterActiveFacts> AttentionOrder { get; } =
        Comparer<ProjectRegisterActiveFacts>.Create(CompareByAttention);

    private int AttentionGroup => IsBlocked ? 0
        : IsLate ? 1
        : CurrentStageState == ProjectRegisterStageState.Finished ? 4
        : PlanStatus == ProjectRegisterPlanStatus.OnPlan ? 2
        : 3;

    private static int CompareByAttention(ProjectRegisterActiveFacts? x, ProjectRegisterActiveFacts? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        var result = x.AttentionGroup.CompareTo(y.AttentionGroup);
        if (result != 0) return result;

        result = (y.DaysLate ?? 0).CompareTo(x.DaysLate ?? 0);
        if (result != 0) return result;

        result = (x.DaysLeft ?? int.MaxValue).CompareTo(y.DaysLeft ?? int.MaxValue);
        if (result != 0) return result;

        return (y.DaysInStage ?? -1).CompareTo(x.DaysInStage ?? -1);
    }

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

        // When every stage is completed or skipped, the ongoing row anchors on the last stage
        // (the project itself is not yet closed): that stage is finished, not waiting.
        var state = current?.Status switch
        {
            StageStatus.InProgress => ProjectRegisterStageState.InProgress,
            StageStatus.Blocked => ProjectRegisterStageState.Blocked,
            StageStatus.Completed or StageStatus.Skipped => ProjectRegisterStageState.Finished,
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
        // A completed or skipped stage keeps its state even when it is the current anchor.
        if (index == currentIndex && status is not (StageStatus.Completed or StageStatus.Skipped))
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

/// <summary>
/// The current stage: under way, blocked, not yet started (waiting on its start), or finished
/// (every stage completed or skipped while the project is still open).
/// </summary>
public enum ProjectRegisterStageState
{
    InProgress,
    Blocked,
    NotStarted,
    Finished
}

public enum ProjectRegisterPlanStatus
{
    NoPdc,
    OnPlan,
    Late
}
