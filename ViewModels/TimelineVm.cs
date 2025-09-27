using System;
using System.Linq;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.ViewModels;

public sealed class TimelineVm
{
    public int ProjectId { get; init; }
    public int TotalStages { get; init; }
    public int CompletedCount { get; init; }
    public IReadOnlyList<TimelineItemVm> Items { get; init; } = Array.Empty<TimelineItemVm>();

    public bool HasBackfill => Items.Any(i => i.RequiresBackfill);
    public bool PlanPendingApproval { get; init; }
    public bool HasDraft { get; init; }
    public DateTimeOffset? LatestApprovalAt { get; init; }
    public string? LatestApprovalBy { get; init; }
}

public sealed class TimelineItemVm
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public StageStatus Status { get; init; } = StageStatus.NotStarted;

    public DateOnly? PlannedStart { get; init; }
    public DateOnly? PlannedEnd { get; init; }
    public DateOnly? ActualStart { get; init; }
    public DateOnly? CompletedOn { get; init; }

    public bool IsAutoCompleted { get; init; }
    public string? AutoCompletedFromCode { get; init; }
    public bool RequiresBackfill { get; init; }

    public int SortOrder { get; init; }
    public int? PlannedDurationDays =>
        (PlannedStart.HasValue && PlannedEnd.HasValue) ? (PlannedEnd.Value.DayNumber - PlannedStart.Value.DayNumber + 1) : null;
    public int? ActualDurationDays =>
        (ActualStart.HasValue && CompletedOn.HasValue) ? (CompletedOn.Value.DayNumber - ActualStart.Value.DayNumber + 1) : null;
}
