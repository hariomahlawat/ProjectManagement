using System;

namespace ProjectManagement.Models.Projects;

// SECTION: Legacy persistence model for completed-project proliferation cost
public class ProjectProductionCostFact
{
    // SECTION: Identity and navigation
    public int ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    // SECTION: Schema-compatible proliferation-cost metadata
    public decimal? ApproxProductionCost { get; set; }
    public string? Remarks { get; set; }

    // SECTION: Audit metadata
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedByUserId { get; set; } = string.Empty;
}
