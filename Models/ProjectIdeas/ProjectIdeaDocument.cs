using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.ProjectIdeas;

public class ProjectIdeaDocument
{
    public int Id { get; set; }
    public int ProjectIdeaId { get; set; }
    public ProjectIdea? ProjectIdea { get; set; }
    [Required, MaxLength(255)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(255)] public string StoredFileName { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string FilePath { get; set; } = string.Empty;
    [MaxLength(100)] public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    [Required] public string UploadedByUserId { get; set; } = string.Empty;
    public ApplicationUser? UploadedByUser { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
