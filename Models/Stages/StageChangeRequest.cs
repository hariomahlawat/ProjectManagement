using System;

namespace ProjectManagement.Models.Stages;

public class StageChangeRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    public string StageCode { get; set; } = string.Empty;
    public string RequestedStatus { get; set; } = string.Empty;
    public DateOnly? RequestedDate { get; set; }
    public string? Note { get; set; }

    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTimeOffset RequestedOn { get; set; }

    public string DecisionStatus { get; set; } = string.Empty;
    public string? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedOn { get; set; }
    public string? DecisionNote { get; set; }
}
