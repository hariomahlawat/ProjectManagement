using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;
using ProjectManagement.Services.Notebook;

namespace ProjectManagement.Contracts.Notebook;

// SECTION: Notebook API request contracts
public class CreateNotebookItemRequest
{
    [StringLength(NotebookLimits.TitleMaxLength)] public string? Title { get; set; }
    [StringLength(NotebookLimits.BodyMaxLength)] public string? Body { get; set; }
    public NotebookItemType Type { get; set; } = NotebookItemType.Note;
    public NotebookPriority Priority { get; set; } = NotebookPriority.Normal;
    public DateTimeOffset? ReminderAtUtc { get; set; }
    [StringLength(32)] public string? ColorKey { get; set; }
    public bool IsPinned { get; set; }
    public List<NotebookChecklistEditRow> ChecklistRows { get; set; } = [];
    public List<string> Labels { get; set; } = [];
}

public sealed class UpdateNotebookItemRequest : CreateNotebookItemRequest
{
    public string? Version { get; set; }
}

public sealed class SetNotebookPinRequest
{
    public bool IsPinned { get; set; }
}

public sealed class ToggleChecklistItemRequest
{
    public bool IsDone { get; set; }

    [Required]
    public Guid Version { get; set; }
}

// SECTION: Notebook API response contracts
public sealed class NotebookChecklistRowResponse
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}

public sealed class NotebookLabelResponse
{
    public string Name { get; set; } = string.Empty;
}

public sealed class NotebookItemResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public NotebookItemType Type { get; set; }
    public NotebookItemStatus Status { get; set; }
    public NotebookPriority Priority { get; set; }
    public bool IsPinned { get; set; }
    public string? ColorKey { get; set; }
    public DateTimeOffset? ReminderAtUtc { get; set; }
    public string? ReminderDisplay { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public List<NotebookChecklistRowResponse> ChecklistRows { get; set; } = [];
    public List<NotebookLabelResponse> Labels { get; set; } = [];
    public string? Version { get; set; }
}
