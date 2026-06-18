using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Notebook;

public sealed class NotebookTodoImportService : INotebookTodoImportService
{
    private readonly ApplicationDbContext _db;
    public NotebookTodoImportService(ApplicationDbContext db) { _db = db; }

    // SECTION: Idempotent lazy import from legacy personal tasks
    public async Task ImportForUserIfRequiredAsync(string ownerId, CancellationToken ct = default)
    {
        if (await _db.NotebookItems.AnyAsync(x => x.OwnerId == ownerId && x.LegacyTodoItemId != null, ct)) return;
        var todos = await _db.TodoItems.AsNoTracking().Where(x => x.OwnerId == ownerId && x.DeletedUtc == null).ToListAsync(ct);
        foreach (var todo in todos)
        {
            _db.NotebookItems.Add(new NotebookItem
            {
                OwnerId = ownerId,
                Title = todo.Title,
                Type = NotebookItemType.Reminder,
                Priority = todo.Priority == TodoPriority.High ? NotebookPriority.High : todo.Priority == TodoPriority.Low ? NotebookPriority.Low : NotebookPriority.Normal,
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
        if (todos.Count > 0) await _db.SaveChangesAsync(ct);
    }
}
