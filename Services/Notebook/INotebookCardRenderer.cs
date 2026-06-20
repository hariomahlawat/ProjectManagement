using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

// SECTION: Notebook card rendering abstraction
public interface INotebookCardRenderer
{
    Task<string> RenderAsync(NotebookItemListVm item, string view, CancellationToken ct = default);
}
