using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Notebook;

public sealed class NotebookTodoImportService : INotebookTodoImportService
{
    private readonly ApplicationDbContext _db;

    public NotebookTodoImportService(ApplicationDbContext db)
    {
        _db = db;
    }

    // SECTION: Idempotent lazy import from legacy personal tasks
    public async Task ImportForUserIfRequiredAsync(string ownerId, CancellationToken ct = default)
    {
        var importedTodoIds = await _db.NotebookItems
            .AsNoTracking()
            .Where(item =>
                item.OwnerId == ownerId &&
                item.LegacyTodoItemId != null)
            .Select(item => item.LegacyTodoItemId!.Value)
            .ToHashSetAsync(ct);

        var todosToImport = await _db.TodoItems
            .AsNoTracking()
            .Where(todo =>
                todo.OwnerId == ownerId &&
                todo.DeletedUtc == null &&
                !importedTodoIds.Contains(todo.Id))
            .OrderBy(todo => todo.CreatedUtc)
            .ToListAsync(ct);

        foreach (var todo in todosToImport)
        {
            _db.NotebookItems.Add(new NotebookItem
            {
                OwnerId = ownerId,
                Title = todo.Title,
                Type = NotebookItemType.Reminder,
                Priority = MapPriority(todo.Priority),
                Status = todo.Status == TodoStatus.Done ? NotebookItemStatus.Completed : NotebookItemStatus.Active,
                ReminderAtUtc = todo.DueAtUtc,
                CompletedAtUtc = todo.CompletedUtc,
                IsPinned = todo.IsPinned,
                ColorKey = "amber",
                SortOrder = todo.OrderIndex,
                CreatedAtUtc = todo.CreatedUtc,
                UpdatedAtUtc = todo.UpdatedUtc,
                LegacyTodoItemId = todo.Id
            });
        }

        if (todosToImport.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    // SECTION: Mapping helpers
    private static NotebookPriority MapPriority(TodoPriority priority) => priority switch
    {
        TodoPriority.High => NotebookPriority.High,
        TodoPriority.Low => NotebookPriority.Low,
        _ => NotebookPriority.Normal
    };
}
