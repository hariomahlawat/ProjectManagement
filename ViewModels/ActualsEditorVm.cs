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

    // Posted back as Input.BaselineVersion; marks a stale-protected (baseline-aware) form.
    public int BaselineVersion { get; init; }
}

public sealed class ActualsEditorRowVm
{
    public string StageCode { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;

    public StageStatus Status { get; init; } = StageStatus.NotStarted;

    public bool IsEditable { get; init; }

    public DateOnly? ActualStart { get; init; }

    public DateOnly? CompletedOn { get; init; }

    public DateOnly? EarliestAllowedStartDate { get; init; }

    public string? StartBoundarySourceName { get; init; }

    public bool IsAutoCompleted { get; init; }

    public bool RequiresBackfill { get; init; }

    public bool HasPendingDecision { get; init; }

    public bool IsArppManaged { get; init; }

    public string? ArppSourceLabel { get; init; }

    public DateOnly? ArppCompletionDate { get; init; }

    // Optimistic identity of the stored row (ActualStageToken); null when no row exists.
    public string? StageToken { get; init; }
}

public sealed class ActualsEditInput
{
    public int ProjectId { get; set; }

    public IList<ActualsEditRowInput> Rows { get; set; } = new List<ActualsEditRowInput>();

    // Absent on forms rendered before stale protection; see ActualStageToken.
    public int? BaselineVersion { get; set; }
}

public sealed class ActualsEditRowInput
{
    public string StageCode { get; set; } = string.Empty;

    public string StageName { get; set; } = string.Empty;

    public DateOnly? ActualStart { get; set; }

    // Additive intent: older callers that omit this flag retain blank-as-preserve semantics.
    public bool ClearActualStart { get; set; }

    public DateOnly? CompletedOn { get; set; }

    // The stored row's token when the form was rendered.
    public string? StageToken { get; set; }
}
