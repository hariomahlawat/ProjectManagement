using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

/// <summary>
/// One ordered hardware requirement / technical specification statement for a project.
/// Structured child rows keep 1-6 publication bullets independently editable and sortable.
/// </summary>
public sealed class ProjectTechnicalSpecificationItem
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    [Required]
    [MaxLength(ProjectFieldLimits.TechnicalSpecificationItemMaxLength)]
    public string Text { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}
