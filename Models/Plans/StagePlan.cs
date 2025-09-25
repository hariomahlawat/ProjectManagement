using System;

namespace ProjectManagement.Models.Plans;

public class StagePlan
{
    public int Id { get; set; }
    public int PlanVersionId { get; set; }
    public PlanVersion? PlanVersion { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedDue { get; set; }
}
