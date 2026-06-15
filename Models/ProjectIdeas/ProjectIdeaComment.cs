using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.ProjectIdeas;

public class ProjectIdeaComment
{
    public int Id { get; set; }
    public int ProjectIdeaId { get; set; }
    public ProjectIdea? ProjectIdea { get; set; }
    [Required, MaxLength(4000)] public string CommentText { get; set; } = string.Empty;
    [Required] public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
