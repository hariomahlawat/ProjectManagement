using System;

namespace ProjectManagement.Models.Projects;

// SECTION: Completed project LPP history model
public class ProjectLppRecord
{
    // SECTION: Identity and navigation
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    // SECTION: LPP metadata
    public decimal LppAmount { get; set; }
    public DateOnly? LppDate { get; set; }
    public string? SupplyOrderNumber { get; set; }

    // SECTION: Supporting artefacts
    public int? ProjectDocumentId { get; set; }
    public ProjectDocument? ProjectDocument { get; set; }

    // SECTION: Additional remarks
    public string? Remarks { get; set; }

    // SECTION: Audit metadata
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
}
