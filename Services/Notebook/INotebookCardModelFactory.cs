using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

// SECTION: Notebook card model creation abstraction
public interface INotebookCardModelFactory
{
    NotebookCardRenderVm Create(NotebookItemListVm item, NotebookCardContext context);
}

// SECTION: Notebook card ambient render context
public sealed class NotebookCardContext
{
    public string View { get; init; } = "home";

    public string? Query { get; init; }

    public string? Filter { get; init; }

    public string? Tag { get; init; }

    public Guid? SelectedId { get; init; }
}
