using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Shared helper that determines the present stage snapshot for a project and its age.
/// </summary>
public static class PresentStageHelper
{
    public static PresentStageSnapshot ComputePresentStageAndAge(
        IReadOnlyList<ProjectStageStatusSnapshot> stages,
        DateOnly? referenceDate = null)
    {
        if (stages is null || stages.Count == 0)
        {
            return PresentStageSnapshot.Empty;
        }

        var orderedStages = stages
            .OrderBy(stage => stage.SortOrder)
            .ToList();

        var current = orderedStages.FirstOrDefault(stage => stage.Status == StageStatus.InProgress)
                      ?? orderedStages.FirstOrDefault(stage => stage.Status != StageStatus.Completed && stage.Status != StageStatus.Skipped)
                      ?? orderedStages.Last();

        var lastCompleted = orderedStages
            .Where(stage => stage.Status == StageStatus.Completed && stage.CompletedOn.HasValue)
            .OrderByDescending(stage => stage.CompletedOn)
            .FirstOrDefault();

        var today = referenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);

        int? days = null;
        if (current.Status == StageStatus.InProgress && current.ActualStart.HasValue)
        {
            days = Math.Max(0, today.DayNumber - current.ActualStart.Value.DayNumber);
        }
        else if (lastCompleted?.CompletedOn is { } completedOn)
        {
            days = Math.Max(0, today.DayNumber - completedOn.DayNumber);
        }

        return new PresentStageSnapshot(
            current.StageCode,
            StageCodes.DisplayNameOf(current.StageCode),
            current.Status == StageStatus.InProgress,
            days,
            current.ActualStart,
            lastCompleted?.CompletedOn);
    }

    public static PresentStageSnapshot ComputePresentStageAndAge(
        int projectId,
        IReadOnlyDictionary<int, IReadOnlyList<ProjectStageStatusSnapshot>> stagesLookup,
        DateOnly? referenceDate = null)
    {
        if (stagesLookup is null || !stagesLookup.TryGetValue(projectId, out var stages))
        {
            return PresentStageSnapshot.Empty;
        }

        return ComputePresentStageAndAge(stages, referenceDate);
    }
}

public readonly record struct ProjectStageStatusSnapshot(
    string StageCode,
    StageStatus Status,
    int SortOrder,
    DateOnly? ActualStart,
    DateOnly? CompletedOn);

public sealed record PresentStageSnapshot(
    string? CurrentStageCode,
    string? CurrentStageName,
    bool IsCurrentStageInProgress,
    int? DaysSinceStartOrLastCompletion,
    DateOnly? CurrentStageStartDate,
    DateOnly? LastCompletedDate)
{
    public static PresentStageSnapshot Empty { get; } = new(null, null, false, null, null, null);
}
