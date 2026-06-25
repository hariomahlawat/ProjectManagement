using ProjectManagement.Models;
using ProjectManagement.ViewModels.Notebook;

// SECTION: My Notebook module types
namespace ProjectManagement.Services.Notebook;

public interface INotebookService
{
    Task<NotebookIndexVm> GetIndexAsync(
        string ownerId,
        string view,
        string? query,
        string? filter,
        string? tag,
        Guid? selectedId,
        CancellationToken ct = default);

    Task<NotebookWidgetVm> GetWidgetAsync(
        string ownerId,
        int take = 5,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, int>> GetCountsAsync(string ownerId, CancellationToken ct = default);

    Task<Guid> QuickCaptureAsync(
        string ownerId,
        string input,
        NotebookItemType? forcedType = null,
        CancellationToken ct = default);

    Task<NotebookItemDetailVm> CreateAsync(string ownerId, NotebookCreateInput input, CancellationToken ct = default);

    Task<NotebookItemDetailVm> CreateAsync(string ownerId, NotebookEditInput input, CancellationToken ct = default);

    Task<NotebookItemDetailVm> UpdateAsync(string ownerId, Guid id, NotebookUpdateInput input, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> UpdateContentAsync(string ownerId, Guid id, string? title, string? body, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> UpdateChecklistAsync(string ownerId, Guid itemId, string? title, string? body, IReadOnlyList<NotebookChecklistEditRow> checklistRows, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> UpdateAsync(string ownerId, Guid id, NotebookEditInput input, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> ArchiveAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> RestoreAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> ReopenAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> DeleteAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> MoveToTrashAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> RestoreFromTrashAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);

    Task DeletePermanentlyAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);

    Task<int> EmptyTrashAsync(string ownerId, CancellationToken ct = default);

    Task<int> PurgeExpiredTrashAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default);


    Task<NotebookItemDetailVm> SetPinnedAsync(string ownerId, Guid id, bool isPinned, Guid expectedVersion, CancellationToken ct = default);

    Task ReorderAsync(string ownerId, NotebookBoardSection section, IReadOnlyList<NotebookOrderItem> items, CancellationToken ct = default);

    Task<NotebookItemDetailVm> SetColourAsync(string ownerId, Guid id, string? colorKey, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> SetLabelsAsync(string ownerId, Guid id, IReadOnlyList<string> labels, Guid expectedVersion, CancellationToken ct = default);

    Task<IReadOnlyList<NotebookTagVm>> GetLabelsAsync(string ownerId, CancellationToken ct = default);

    Task<NotebookTagVm> CreateLabelAsync(string ownerId, string name, CancellationToken ct = default);

    Task<(IReadOnlyList<NotebookTagVm> Labels, IReadOnlyList<Guid> AffectedItemIds)> RenameLabelAsync(string ownerId, int labelId, string name, CancellationToken ct = default);

    Task<(IReadOnlyList<NotebookTagVm> Labels, IReadOnlyList<Guid> AffectedItemIds)> DeleteLabelAsync(string ownerId, int labelId, CancellationToken ct = default);

    Task<NotebookItemDetailVm?> GetDetailAsync(string ownerId, Guid id, CancellationToken ct = default);

    Task<NotebookItemDetailVm> CompleteAsync(string ownerId, Guid id, bool isComplete, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> ConvertTypeAsync(string ownerId, Guid id, NotebookItemType newType, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> DuplicateAsync(string ownerId, Guid id, CancellationToken ct = default);


    Task<NotebookItemDetailVm> ToggleChecklistItemAsync(string ownerId, Guid itemId, int checklistItemId, bool isDone, Guid expectedVersion, CancellationToken ct = default);

    Task<IReadOnlyList<NotebookCollaboratorVm>> GetCollaboratorsAsync(string userId, Guid itemId, CancellationToken ct = default);
    Task<IReadOnlyList<NotebookCollaboratorSearchVm>> SearchCollaboratorsAsync(string userId, Guid itemId, string query, int take = 10, CancellationToken ct = default);
    Task<NotebookItemDetailVm> AddCollaboratorAsync(string ownerId, Guid itemId, string collaboratorUserId, NotebookCollaborationRole role, Guid expectedVersion, CancellationToken ct = default);
    Task<NotebookItemDetailVm> RemoveCollaboratorAsync(string ownerId, Guid itemId, string collaboratorUserId, Guid expectedVersion, CancellationToken ct = default);
    Task LeaveCollaborationAsync(string userId, Guid itemId, CancellationToken ct = default);
}

// SECTION: My Notebook command input contracts
public sealed class NotebookCreateInput
{
    public Guid? ClientRequestId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? BodyMarkdown { get; init; }
    public NotebookItemType Type { get; init; } = NotebookItemType.Note;
    public NotebookPriority Priority { get; init; } = NotebookPriority.Normal;
    public DateTimeOffset? ReminderAtUtc { get; init; }
    public bool IsPinned { get; init; }
    public bool IsFavorite { get; init; }
    public string? ColorKey { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ChecklistItems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NotebookChecklistEditRow> ChecklistRows { get; init; } = Array.Empty<NotebookChecklistEditRow>();
}

public sealed class NotebookUpdateInput
{
    public string Title { get; init; } = string.Empty;
    public string? BodyMarkdown { get; init; }
    public NotebookPriority? Priority { get; init; }
    public DateTimeOffset? ReminderAtUtc { get; init; }
    public string? ColorKey { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ChecklistItems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NotebookChecklistEditRow> ChecklistRows { get; init; } = Array.Empty<NotebookChecklistEditRow>();
}

// SECTION: Legacy My Notebook editor contract
public sealed class NotebookEditInput
{
    public string Title { get; set; } = string.Empty;

    public string? BodyMarkdown { get; set; }

    public NotebookItemType Type { get; set; } = NotebookItemType.Note;

    public NotebookPriority Priority { get; set; } = NotebookPriority.Normal;

    public DateTimeOffset? ReminderAtUtc { get; set; }

    public string? ReminderLocal { get; set; }

    public bool IsPinned { get; set; }

    public bool IsFavorite { get; set; }

    public string? ColorKey { get; set; }

    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> ChecklistItems { get; set; } = Array.Empty<string>();

    public IReadOnlyList<NotebookChecklistEditRow> ChecklistRows { get; set; } = Array.Empty<NotebookChecklistEditRow>();

    public Guid? ClientRequestId { get; set; }
}

public sealed class NotebookChecklistEditRow
{
    public int? Id { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(100)]
    public string? ClientKey { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(NotebookLimits.ChecklistTextMaxLength)]
    public string Text { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
    public int SortOrder { get; set; }
}

public enum NotebookBoardSection
{
    Pinned = 0,
    Others = 1
}

public sealed record NotebookOrderItem(Guid Id, Guid Version);
