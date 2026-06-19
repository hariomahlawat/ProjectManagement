using System.ComponentModel.DataAnnotations;
using ProjectManagement.Services.Notebook;

// SECTION: My Notebook module types
namespace ProjectManagement.Models;

public class NotebookChecklistItem
{
    public int Id { get; set; }
    public Guid NotebookItemId { get; set; }
    public NotebookItem? NotebookItem { get; set; }
    [Required, MaxLength(NotebookLimits.ChecklistTextMaxLength)] public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
