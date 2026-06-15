using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.ProjectIdeas;

public class ProjectIdeaNote
{
    public int Id { get; set; }
    public int ProjectIdeaId { get; set; }
    public ProjectIdea? ProjectIdea { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required] public string Body { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    [Required] public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
