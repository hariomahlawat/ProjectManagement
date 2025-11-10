using System;

namespace ProjectManagement.Models.Plans;

// SECTION: Realignment audit entity
public sealed class PlanRealignmentAudit
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int PlanVersionNo { get; set; }
    public string SourceStageCode { get; set; } = default!;
    public int DelayDays { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = default!;
}
