using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public class ProjectAudit
{
    public int Id { get; set; }

    [Required]
    public int ProjectId { get; set; }

    public Project? Project { get; set; }

    [Required]
    [MaxLength(32)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [MaxLength(450)]
    public string PerformedByUserId { get; set; } = string.Empty;

    public ApplicationUser? PerformedByUser { get; set; }

    public DateTimeOffset PerformedAt { get; set; }

    [MaxLength(512)]
    public string? Reason { get; set; }

    [MaxLength(4000)]
    public string? MetadataJson { get; set; }
}
