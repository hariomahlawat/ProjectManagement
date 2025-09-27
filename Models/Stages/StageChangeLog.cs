using System;

namespace ProjectManagement.Models.Stages;

public class StageChangeLog
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    public string StageCode { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }

    public DateOnly? FromActualStart { get; set; }
    public DateOnly? ToActualStart { get; set; }

    public DateOnly? FromCompletedOn { get; set; }
    public DateOnly? ToCompletedOn { get; set; }

    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public string? Note { get; set; }
}
