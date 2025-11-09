using System;

namespace ProjectManagement.Models.Projects;

// SECTION: Completed project production cost fact model
public class ProjectProductionCostFact
{
    // SECTION: Identity and navigation
    public int ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    // SECTION: Production cost metadata
    public decimal? ApproxProductionCost { get; set; }
    public string? Remarks { get; set; }

    // SECTION: Audit metadata
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedByUserId { get; set; } = string.Empty;
}
