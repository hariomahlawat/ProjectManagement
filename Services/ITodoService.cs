using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public interface ITodoService
    {
        Task<TodoWidgetResult> GetWidgetAsync(string ownerId, int take = 20);
        Task<TodoItem> CreateAsync(string ownerId, string title, DateTimeOffset? dueAtLocal = null,
                                   TodoPriority priority = TodoPriority.Normal, bool pinned = false);
        Task<bool> ToggleDoneAsync(string ownerId, Guid id, bool done);
        Task<bool> EditAsync(string ownerId, Guid id, string? title = null,
                              DateTimeOffset? dueAtLocal = null, TodoPriority? priority = null, bool? pinned = null);
        Task<bool> DeleteAsync(string ownerId, Guid id);
        Task<int> ClearCompletedAsync(string ownerId);
        Task<bool> ReorderAsync(string ownerId, IList<Guid> orderedIds);
        Task MarkDoneAsync(string ownerId, IList<Guid> ids);
        Task DeleteManyAsync(string ownerId, IList<Guid> ids);
    }

    public class TodoWidgetResult
    {
        public IList<TodoItem> Items { get; set; } = new List<TodoItem>();
        public int OverdueCount { get; set; }
        public int DueTodayCount { get; set; }
    }
}
