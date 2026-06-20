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

    Task<NotebookItemDetailVm> CreateAsync(string ownerId, NotebookEditInput input, CancellationToken ct = default);

    Task<NotebookItemDetailVm> UpdateAsync(string ownerId, Guid id, NotebookEditInput input, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> ArchiveAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> RestoreAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> ReopenAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> DeleteAsync(string ownerId, Guid id, Guid expectedVersion, CancellationToken ct = default);


    Task<NotebookItemDetailVm> SetPinnedAsync(string ownerId, Guid id, bool isPinned, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm?> GetDetailAsync(string ownerId, Guid id, CancellationToken ct = default);

    Task<NotebookItemDetailVm> CompleteAsync(string ownerId, Guid id, bool isComplete, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> ConvertTypeAsync(string ownerId, Guid id, NotebookItemType newType, Guid expectedVersion, CancellationToken ct = default);

    Task<NotebookItemDetailVm> DuplicateAsync(string ownerId, Guid id, CancellationToken ct = default);


    Task<NotebookItemDetailVm> ToggleChecklistItemAsync(string ownerId, Guid itemId, int checklistItemId, bool isDone, Guid expectedVersion, CancellationToken ct = default);
}

// SECTION: My Notebook editor contract
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

    public string Text { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public int SortOrder { get; set; }
}
