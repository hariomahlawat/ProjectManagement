using ProjectManagement.ViewModels.Notebook;

namespace ProjectManagement.Services.Notebook;

// SECTION: Notebook card model creation abstraction
public interface INotebookCardModelFactory
{
    NotebookCardRenderVm Create(NotebookItemListVm item, NotebookCardContext context);
}

// SECTION: Supported notebook card render contexts
public static class NotebookCardContexts
{
    public const string Home = "home";
}

// SECTION: Notebook card ambient render context
public sealed class NotebookCardContext
{
    public string View { get; init; } = NotebookCardContexts.Home;

    public string? Query { get; init; }

    public string? Filter { get; init; }

    public string? Tag { get; init; }

    public Guid? SelectedId { get; init; }
}
