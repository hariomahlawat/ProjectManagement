using ProjectManagement.Models.Stages;

namespace ProjectManagement.ViewModels;

public sealed class TimelineVm
{
    public int ProjectId { get; init; }
    public int TotalStages { get; init; }
    public int CompletedCount { get; init; }
    public IReadOnlyList<TimelineItemVm> Items { get; init; } = Array.Empty<TimelineItemVm>();
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

    public int SortOrder { get; init; }
    public int? PlannedDurationDays =>
        (PlannedStart.HasValue && PlannedEnd.HasValue) ? (PlannedEnd.Value.DayNumber - PlannedStart.Value.DayNumber + 1) : null;
    public int? ActualDurationDays =>
        (ActualStart.HasValue && CompletedOn.HasValue) ? (CompletedOn.Value.DayNumber - ActualStart.Value.DayNumber + 1) : null;
}
