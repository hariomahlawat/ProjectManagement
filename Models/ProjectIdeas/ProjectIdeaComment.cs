using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.ProjectIdeas;

public static class ProjectIdeaCommentTypes
{
    public const string General = "General";
    public const string Conference = "Conference";

    public static readonly IReadOnlyList<string> All = new[]
    {
        General,
        Conference
    };
}

public class ProjectIdeaComment
{
    public int Id { get; set; }

    public int ProjectIdeaId { get; set; }
    public ProjectIdea? ProjectIdea { get; set; }

    [Required, MaxLength(4000)]
    public string CommentText { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string CommentType { get; set; } = ProjectIdeaCommentTypes.General;

    [Required]
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }

    [MaxLength(64)]
    public string? CreatedByRole { get; set; }

    [MaxLength(32)]
    public string? StatusSnapshot { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
