using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.ProjectIdeas;

public class ProjectIdea
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string Description { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Status { get; set; } = ProjectIdeaStatuses.Active;
    public string? AssignedProjectOfficerUserId { get; set; }
    public ApplicationUser? AssignedProjectOfficerUser { get; set; }
    public string? AssignedHodUserId { get; set; }
    public ApplicationUser? AssignedHodUser { get; set; }
    [Required] public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }
    [MaxLength(1000)] public string? ArchiveReason { get; set; }
    public bool IsDeleted { get; set; }
    public ICollection<ProjectIdeaComment> Comments { get; set; } = new List<ProjectIdeaComment>();
    public ICollection<ProjectIdeaNote> Notes { get; set; } = new List<ProjectIdeaNote>();
    public ICollection<ProjectIdeaDocument> Documents { get; set; } = new List<ProjectIdeaDocument>();
}
