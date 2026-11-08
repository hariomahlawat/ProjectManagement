using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Models.ItProjects;

public enum ItProjectStatusKind
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Dropped = 3
}

/// <summary>
/// The derived status of an IT project. Never stored: it is resolved from the stage rows and
/// the Dropped marker every time it is read (docs/ux/it-projects.md, "Derived status").
/// </summary>
public sealed record ItProjectStatus(ItProjectStatusKind Kind, string? CurrentStageCode)
{
    public string? CurrentStageLabel => CurrentStageCode is null ? null : ItProjectStages.LabelOf(CurrentStageCode);

    public string Label => Kind switch
    {
        ItProjectStatusKind.NotStarted => "Not started",
        ItProjectStatusKind.InProgress => $"In progress · {CurrentStageLabel}",
        ItProjectStatusKind.Completed => "Completed",
        ItProjectStatusKind.Dropped => "Dropped",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null)
    };

    public static ItProjectStatus Resolve(ItProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Resolve(project.DroppedOn.HasValue, project.Stages);
    }

    public static ItProjectStatus Resolve(bool isDropped, IEnumerable<ItProjectStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var ordered = stages.OrderBy(stage => stage.SortOrder).ToList();
        var current = ordered.FirstOrDefault(stage => !stage.IsSkipped && !stage.IsCompleted);

        if (isDropped)
        {
            return new ItProjectStatus(ItProjectStatusKind.Dropped, current?.StageCode);
        }

        if (!ordered.Any(stage => stage.IsCompleted))
        {
            return new ItProjectStatus(ItProjectStatusKind.NotStarted, current?.StageCode);
        }

        if (current is null)
        {
            return new ItProjectStatus(ItProjectStatusKind.Completed, null);
        }

        return new ItProjectStatus(ItProjectStatusKind.InProgress, current.StageCode);
    }
}
