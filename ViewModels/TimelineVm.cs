using System;
using System.Linq;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.ViewModels;

public sealed class TimelineVm
{
    public int ProjectId { get; init; }
    public int TotalStages { get; init; }
    public int CompletedCount { get; init; }
    public IReadOnlyList<TimelineItemVm> Items { get; init; } = Array.Empty<TimelineItemVm>();
    public IReadOnlyList<TimelineStageRequestVm> PendingRequests { get; init; } = Array.Empty<TimelineStageRequestVm>();

    public bool HasBackfill => Items.Any(i => i.RequiresBackfill);
    public bool PlanPendingApproval { get; init; }
    public bool HasDraft { get; init; }
    public DateTimeOffset? LatestApprovalAt { get; init; }
    public string? LatestApprovalBy { get; init; }
}

public sealed class TimelineStageRequestVm
{
    public int RequestId { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public string StageName { get; init; } = string.Empty;
    public StageStatus CurrentStatus { get; init; } = StageStatus.NotStarted;
    public string RequestedStatus { get; init; } = string.Empty;
    public DateOnly? RequestedDate { get; init; }
    public string? Note { get; init; }
    public string RequestedBy { get; init; } = string.Empty;
    public DateTimeOffset RequestedOn { get; init; }
}

public sealed class TimelineItemVm
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public StageStatus Status { get; init; } = StageStatus.NotStarted;

    public DateOnly? PlannedStart { get; init; }
    public DateOnly? PlannedEnd { get; init; }
    public DateOnly? ActualStart { get; init; }
    public DateOnly? EffectiveActualStart { get; init; }
    public bool IsActualStartInferred { get; init; }
    public DateOnly? CompletedOn { get; init; }

    public bool IsAutoCompleted { get; init; }
    public string? AutoCompletedFromCode { get; init; }
    public bool RequiresBackfill { get; init; }
    public bool HasPendingRequest { get; init; }
    public string? PendingStatus { get; init; }
    public DateOnly? PendingDate { get; init; }
    public int? PendingRequestId { get; init; }

    public int? StartVarianceDays { get; init; }
    public int? FinishVarianceDays { get; init; }

    public DateOnly Today { get; init; }

    public int SortOrder { get; init; }
    public int? PlannedDurationDays =>
        (PlannedStart.HasValue && PlannedEnd.HasValue) ? (PlannedEnd.Value.DayNumber - PlannedStart.Value.DayNumber + 1) : null;
    public int? ActualDurationDays =>
        (EffectiveActualStart.HasValue && CompletedOn.HasValue)
            ? Math.Max(1, CompletedOn.Value.DayNumber - EffectiveActualStart.Value.DayNumber + 1)
            : null;

    // Completed stages are completion-driven. Actual start is optional and may be inferred.
    public bool NeedsStart => Status == StageStatus.InProgress && ActualStart is null;
    public bool NeedsFinish => Status == StageStatus.Completed && CompletedOn is null;
    public bool NeedsPlannedCompletion => Status == StageStatus.InProgress && PlannedEnd is null;
    public bool IsIncompleteData => NeedsStart || NeedsFinish || NeedsPlannedCompletion;
    public bool IsOverdue => Status == StageStatus.InProgress && PlannedEnd.HasValue && Today > PlannedEnd.Value;

    public bool HasPlanDates => PlannedStart.HasValue || PlannedEnd.HasValue;
    public bool HasActualDates => ActualStart.HasValue || CompletedOn.HasValue;
    public bool ShowPlanWarning => Status == StageStatus.InProgress && PlannedEnd is null;
    public bool ShowFinishVariance => CompletedOn.HasValue && PlannedEnd.HasValue;
    public int? DaysRemaining => Status == StageStatus.InProgress && PlannedEnd.HasValue
        ? PlannedEnd.Value.DayNumber - Today.DayNumber
        : null;
}
