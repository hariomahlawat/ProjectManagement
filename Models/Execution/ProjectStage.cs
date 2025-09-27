using System;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Execution;

public enum StageStatus
{
    NotStarted = 1,
    InProgress = 2,
    Completed = 3,
    Skipped = 4,
    Blocked = 5
}

public class ProjectStage
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string StageCode { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public StageStatus Status { get; set; } = StageStatus.NotStarted;

    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedDue { get; set; }

    public DateOnly? ForecastStart { get; set; }
    public DateOnly? ForecastDue { get; set; }

    public DateOnly? ActualStart { get; set; }
    public DateOnly? CompletedOn { get; set; }

    public bool IsAutoCompleted { get; set; }
    public string? AutoCompletedFromCode { get; set; }
    public bool RequiresBackfill { get; set; }
}
