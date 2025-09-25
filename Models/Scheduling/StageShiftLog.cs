using System;

namespace ProjectManagement.Models.Scheduling;

public class StageShiftLog
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    public string StageCode { get; set; } = string.Empty;
    public DateOnly? OldForecastDue { get; set; }
    public DateOnly NewForecastDue { get; set; }
    public int DeltaDays { get; set; }

    public string CauseStageCode { get; set; } = string.Empty;
    public string CauseType { get; set; } = string.Empty;

    public DateTimeOffset CreatedOn { get; set; }
    public string? CreatedByUserId { get; set; }
}
