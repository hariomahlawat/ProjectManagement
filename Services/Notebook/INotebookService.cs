using ProjectManagement.Models;
using ProjectManagement.ViewModels.Notebook;

// SECTION: My Notebook module types
namespace ProjectManagement.Services.Notebook;

public interface INotebookService
{
    Task<NotebookIndexVm> GetIndexAsync(string ownerId, string view, string? query, Guid? selectedId, CancellationToken ct = default);

    Task<Guid> QuickCaptureAsync(string ownerId, string input, NotebookItemType? forcedType = null, CancellationToken ct = default);

    Task<Guid> CreateAsync(string ownerId, NotebookEditInput input, CancellationToken ct = default);

    Task UpdateAsync(string ownerId, Guid id, NotebookEditInput input, CancellationToken ct = default);

    Task ArchiveAsync(string ownerId, Guid id, CancellationToken ct = default);

    Task RestoreAsync(string ownerId, Guid id, CancellationToken ct = default);

    Task DeleteAsync(string ownerId, Guid id, CancellationToken ct = default);

    Task TogglePinAsync(string ownerId, Guid id, CancellationToken ct = default);

    Task ToggleFavoriteAsync(string ownerId, Guid id, CancellationToken ct = default);

    Task CompleteAsync(string ownerId, Guid id, bool isComplete, CancellationToken ct = default);

    Task ConvertTypeAsync(string ownerId, Guid id, NotebookItemType newType, CancellationToken ct = default);

    Task ToggleChecklistItemAsync(string ownerId, int checklistItemId, bool isDone, CancellationToken ct = default);
}
