using System.ComponentModel.DataAnnotations;

// SECTION: My Notebook module types
namespace ProjectManagement.Models;

public class NotebookAttachment
{
    public int Id { get; set; }
    public Guid NotebookItemId { get; set; }
    public NotebookItem? NotebookItem { get; set; }
    [Required, MaxLength(255)] public string OriginalFileName { get; set; } = string.Empty;
    [Required, MaxLength(512)] public string RelativePath { get; set; } = string.Empty;
    [MaxLength(128)] public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    [Required] public string UploadedById { get; set; } = string.Empty;
    public ApplicationUser? UploadedBy { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; }
}
