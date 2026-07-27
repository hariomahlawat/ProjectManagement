using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

/// <summary>
/// One ordered, presentation-ready capability statement for a project.
/// The statement is intentionally structured as a child record so that
/// ordering, validation and PowerPoint bullet generation remain reliable.
/// </summary>
public sealed class ProjectCapabilityStatement
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    [Required]
    [MaxLength(ProjectFieldLimits.CapabilityStatementMaxLength)]
    public string Statement { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}
