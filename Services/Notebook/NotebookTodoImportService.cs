using System.Data;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Notebook;

public sealed class NotebookTodoImportService : INotebookTodoImportService
{
    public const string MigrationKey = "LegacyTodoImportV1";

    private readonly ApplicationDbContext _db;

    public NotebookTodoImportService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // SECTION: One-time, transaction-safe legacy Todo import fallback
    // Normal Notebook page loads no longer invoke this service. The deployment
    // migration performs the set-based import; this method remains available for
    // older installations and controlled repair scenarios.
    public async Task ImportForUserIfRequiredAsync(string ownerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("A valid owner id is required.", nameof(ownerId));
        }

        if (await _db.NotebookMigrationStates
            .AsNoTracking()
            .AnyAsync(state => state.UserId == ownerId && state.MigrationKey == MigrationKey, ct))
        {
            return;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (await _db.NotebookMigrationStates
            .AnyAsync(state => state.UserId == ownerId && state.MigrationKey == MigrationKey, ct))
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var todosToImport = await _db.TodoItems
            .AsNoTracking()
            .Where(todo =>
                todo.OwnerId == ownerId &&
                todo.DeletedUtc == null &&
                !_db.NotebookItems.Any(item =>
                    item.OwnerId == ownerId &&
                    item.LegacyTodoItemId == todo.Id))
            .OrderBy(todo => todo.OrderIndex)
            .ThenBy(todo => todo.CreatedUtc)
            .ToListAsync(ct);

        var nextSortOrder = new Dictionary<bool, int>();
        foreach (var isPinned in new[] { false, true })
        {
            var maximum = await _db.NotebookItems
                .Where(item =>
                    item.OwnerId == ownerId &&
                    item.DeletedAtUtc == null &&
                    item.Status == NotebookItemStatus.Active &&
                    item.IsPinned == isPinned)
                .Select(item => (int?)item.SortOrder)
                .MaxAsync(ct);

            nextSortOrder[isPinned] = maximum.HasValue
                ? checked(maximum.Value + NotebookLimits.SortOrderStep)
                : 0;
        }

        foreach (var todo in todosToImport)
        {
            _db.NotebookItems.Add(new NotebookItem
            {
                OwnerId = ownerId,
                Title = todo.Title,
                // Reminder is metadata; imported tasks persist as ordinary notes.
                Type = NotebookItemType.Note,
                Priority = MapPriority(todo.Priority),
                Status = todo.Status == TodoStatus.Done ? NotebookItemStatus.Completed : NotebookItemStatus.Active,
                ReminderAtUtc = todo.DueAtUtc,
                CompletedAtUtc = todo.CompletedUtc,
                IsPinned = todo.IsPinned,
                ColorKey = "amber",
                SortOrder = nextSortOrder[todo.IsPinned],
                CreatedAtUtc = todo.CreatedUtc,
                UpdatedAtUtc = todo.UpdatedUtc,
                Version = Guid.NewGuid(),
                LegacyTodoItemId = todo.Id
            });

            nextSortOrder[todo.IsPinned] = checked(nextSortOrder[todo.IsPinned] + NotebookLimits.SortOrderStep);
        }

        _db.NotebookMigrationStates.Add(new NotebookMigrationState
        {
            UserId = ownerId,
            MigrationKey = MigrationKey,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            ImportedCount = todosToImport.Count
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // SECTION: Mapping helpers
    private static NotebookPriority MapPriority(TodoPriority priority) => priority switch
    {
        TodoPriority.High => NotebookPriority.High,
        TodoPriority.Low => NotebookPriority.Low,
        _ => NotebookPriority.Normal
    };
}
