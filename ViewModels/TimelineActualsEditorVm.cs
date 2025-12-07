using System;
using System.Collections.Generic;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.ViewModels;

public sealed class TimelineActualsEditorVm
{
    public static readonly TimelineActualsEditorVm Empty = new()
    {
        Rows = Array.Empty<TimelineActualsEditorRowVm>()
    };

    public int ProjectId { get; init; }

    public DateOnly Today { get; init; }

    public IReadOnlyList<TimelineActualsEditorRowVm> Rows { get; init; } = Array.Empty<TimelineActualsEditorRowVm>();
}

public sealed class TimelineActualsEditorRowVm
{
    public string StageCode { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;

    public StageStatus Status { get; init; } = StageStatus.NotStarted;

    public DateOnly? ActualStart { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public bool IsAutoCompleted { get; init; }

    public bool RequiresBackfill { get; init; }
}
