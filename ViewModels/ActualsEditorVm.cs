using System;
using System.Collections.Generic;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.ViewModels;

// SECTION: Actuals editor view models
public sealed class ActualsEditorVm
{
    public static readonly ActualsEditorVm Empty = new()
    {
        Rows = Array.Empty<ActualsEditorRowVm>()
    };

    public int ProjectId { get; init; }

    public DateOnly Today { get; init; }

    public IReadOnlyList<ActualsEditorRowVm> Rows { get; init; } = Array.Empty<ActualsEditorRowVm>();
}

public sealed class ActualsEditorRowVm
{
    public string StageCode { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;

    public StageStatus Status { get; init; } = StageStatus.NotStarted;

    public bool IsEditable { get; init; }

    public DateOnly? ActualStart { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public bool IsAutoCompleted { get; init; }

    public bool RequiresBackfill { get; init; }

    public bool HasPendingDecision { get; init; }
}

public sealed class ActualsEditInput
{
    public int ProjectId { get; set; }

    public IList<ActualsEditRowInput> Rows { get; set; } = new List<ActualsEditRowInput>();
}

public sealed class ActualsEditRowInput
{
    public string StageCode { get; set; } = string.Empty;

    public string StageName { get; set; } = string.Empty;

    public DateOnly? ActualStart { get; set; }

    public DateOnly? CompletedOn { get; set; }
}
