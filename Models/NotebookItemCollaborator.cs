using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models;

public enum NotebookCollaborationRole : byte
{
    Editor = 0,
    Viewer = 1
}

public sealed class NotebookItemCollaborator
{
    public Guid NotebookItemId { get; set; }
    public NotebookItem NotebookItem { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public NotebookCollaborationRole Role { get; set; } = NotebookCollaborationRole.Editor;

    [Required]
    public string AddedByUserId { get; set; } = string.Empty;
    public ApplicationUser AddedByUser { get; set; } = null!;

    public DateTimeOffset AddedAtUtc { get; set; }

    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
}
