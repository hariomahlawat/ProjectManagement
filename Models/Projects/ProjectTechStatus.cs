using System;

namespace ProjectManagement.Models.Projects;

// SECTION: Completed project technology status model
public class ProjectTechStatus
{
    // SECTION: Identity and navigation
    public int ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    // SECTION: Status metadata
    public string TechStatus { get; set; } = ProjectTechStatusCodes.Current;
    public bool AvailableForProliferation { get; set; }

    // SECTION: Remarks and notes
    public string? NotAvailableReason { get; set; }
    public string? Remarks { get; set; }

    // SECTION: Audit metadata
    public DateTimeOffset MarkedAtUtc { get; set; }
    public string MarkedByUserId { get; set; } = string.Empty;
}
