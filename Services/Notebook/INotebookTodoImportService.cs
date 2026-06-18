namespace ProjectManagement.Services.Notebook;

// SECTION: My Notebook module types
public interface INotebookTodoImportService
{
    Task ImportForUserIfRequiredAsync(string ownerId, CancellationToken ct = default);
}
