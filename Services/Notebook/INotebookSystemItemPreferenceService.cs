using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

public sealed class NotebookSystemItemPreferencePatch
{
    public bool? ShowInHome { get; init; }
    public bool? IsPinned { get; init; }
    public string? ColorKey { get; init; }
    public IReadOnlyList<string>? Labels { get; init; }
}

public interface INotebookSystemItemPreferenceService
{
    Task<NotebookSystemItemPreferenceVm> GetAsync(string userId, string systemItemKey, CancellationToken ct = default);
    Task<NotebookSystemItemPreferenceVm> UpdateAsync(string userId, string systemItemKey, NotebookSystemItemPreferencePatch patch, CancellationToken ct = default);
    Task<NotebookSystemItemPreferenceVm> SetPlacementAsync(string userId, string systemItemKey, bool isPinned, int position, CancellationToken ct = default);
}
