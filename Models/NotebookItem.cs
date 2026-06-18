using System.ComponentModel.DataAnnotations;

// SECTION: My Notebook module types
namespace ProjectManagement.Models;

public enum NotebookItemType : byte
{
    Note = 0,
    Sticky = 1,
    Checklist = 2,
    Reminder = 3,
    Idea = 4,
    Draft = 5
}

public enum NotebookItemStatus : byte
{
    Active = 0,
    Completed = 1,
    Archived = 2
}

public enum NotebookPriority : byte
{
    Low = 0,
    Normal = 1,
    High = 2
}

public class NotebookItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string OwnerId { get; set; } = string.Empty;

    public ApplicationUser? Owner { get; set; }

    [Required]
    [MaxLength(220)]
    public string Title { get; set; } = string.Empty;

    public string? BodyMarkdown { get; set; }

    public NotebookItemType Type { get; set; } = NotebookItemType.Note;

    public NotebookItemStatus Status { get; set; } = NotebookItemStatus.Active;

    public NotebookPriority Priority { get; set; } = NotebookPriority.Normal;

    public DateTimeOffset? ReminderAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public bool IsPinned { get; set; }

    public bool IsFavorite { get; set; }

    [MaxLength(24)]
    public string? ColorKey { get; set; }

    public int SortOrder { get; set; }

    public Guid? LegacyTodoItemId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? ArchivedAtUtc { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }

    public ICollection<NotebookChecklistItem> ChecklistItems { get; set; } = new List<NotebookChecklistItem>();

    public ICollection<NotebookItemTag> Tags { get; set; } = new List<NotebookItemTag>();

    public ICollection<NotebookAttachment> Attachments { get; set; } = new List<NotebookAttachment>();
}
