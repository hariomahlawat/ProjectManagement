using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Stages;

public enum TrackNodeState
{
    Done,
    Current,
    Todo
}

public sealed class TrackNodeVm
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public TrackNodeState State { get; init; }
        = TrackNodeState.Todo;
    public DateOnly? PlannedStart { get; init; }
        = null;
    public DateOnly? PlannedDue { get; init; }
        = null;
    public DateOnly? ActualStart { get; init; }
        = null;
    public DateOnly? CompletedOn { get; init; }
        = null;
    public int SlipDays { get; init; }
        = 0;
    public string Tooltip { get; init; } = string.Empty;
    public bool IsOptional { get; init; }
        = false;
    public bool IsVisible { get; init; } = true;
}

public sealed class TrackEdgeVm
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Variant { get; init; } = "neutral";
}

public sealed class TrackerVm
{
    public List<TrackNodeVm> Main { get; init; } = new();
    public List<TrackNodeVm> BranchTop { get; init; } = new();
    public List<TrackNodeVm> BranchBottom { get; init; } = new();
    public List<TrackEdgeVm> Edges { get; init; } = new();
    public string CurrentCode { get; init; } = string.Empty;
    public bool PncApplicable { get; init; } = true;
}
