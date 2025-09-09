using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class TodoService : ITodoService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuditService _audit;
        private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        public TodoService(ApplicationDbContext db, IAuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        private static DateTimeOffset? ToUtc(DateTimeOffset? localIst)
        {
            if (localIst is null) return null;
            var stamp = new DateTimeOffset(
                localIst.Value.Year,
                localIst.Value.Month,
                localIst.Value.Day,
                localIst.Value.Hour,
                localIst.Value.Minute,
                0,
                Ist.GetUtcOffset(localIst.Value));
            return TimeZoneInfo.ConvertTime(stamp, TimeZoneInfo.Utc);
        }

        public async Task<TodoWidgetResult> GetWidgetAsync(string ownerId, int take = 20)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var nowIst = TimeZoneInfo.ConvertTime(nowUtc, Ist);
            var todayStartIst = new DateTimeOffset(nowIst.Year, nowIst.Month, nowIst.Day, 0, 0, 0, Ist.BaseUtcOffset);
            var todayEndIst = todayStartIst.AddDays(1).AddTicks(-1);

            var todayStartUtc = TimeZoneInfo.ConvertTime(todayStartIst, TimeZoneInfo.Utc);
            var todayEndUtc = TimeZoneInfo.ConvertTime(todayEndIst, TimeZoneInfo.Utc);

            var query = _db.TodoItems.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.Status == TodoStatus.Open && x.DeletedUtc == null);

            var overdueCount = await query.Where(x => x.DueAtUtc < nowUtc).CountAsync();
            var todayCount = await query.Where(x => x.DueAtUtc >= todayStartUtc && x.DueAtUtc <= todayEndUtc).CountAsync();

            var items = await query
                .OrderByDescending(x => x.IsPinned)
                .ThenBy(x => x.DueAtUtc == null)
                .ThenBy(x => x.DueAtUtc)
                .ThenBy(x => x.OrderIndex)
                .ThenBy(x => x.CreatedUtc)
                .Take(take)
                .ToListAsync();

            return new TodoWidgetResult
            {
                Items = items,
                OverdueCount = overdueCount,
                TodayCount = todayCount
            };
        }

        public async Task<TodoItem> CreateAsync(string ownerId, string title, DateTimeOffset? dueAtLocal = null,
                                   TodoPriority priority = TodoPriority.Normal, bool pinned = false, string? notes = null)
        {
            var utcDue = ToUtc(dueAtLocal);
            var last = await _db.TodoItems.Where(x => x.OwnerId == ownerId && x.DeletedUtc == null).MaxAsync(x => (int?)x.OrderIndex) ?? -1;
            var item = new TodoItem
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                Title = title,
                Notes = notes,
                DueAtUtc = utcDue,
                Priority = priority,
                IsPinned = pinned,
                Status = TodoStatus.Open,
                OrderIndex = last + 1,
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            _db.TodoItems.Add(item);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Todo.Create", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = item.Id.ToString() });
            return item;
        }

        public async Task<bool> ToggleDoneAsync(string ownerId, Guid id, bool done)
        {
            var item = await _db.TodoItems.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId && x.DeletedUtc == null);
            if (item == null) return false;
            if (done && item.Status == TodoStatus.Open)
            {
                item.Status = TodoStatus.Done;
                item.CompletedUtc = DateTimeOffset.UtcNow;
                await _audit.LogAsync("Todo.Done", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            }
            else if (!done && item.Status == TodoStatus.Done)
            {
                item.Status = TodoStatus.Open;
                item.CompletedUtc = null;
                await _audit.LogAsync("Todo.Undone", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            }
            item.UpdatedUtc = DateTimeOffset.UtcNow;
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException("This task was modified by another session. Please refresh and try again.");
            }
            return true;
        }

        public async Task<bool> EditAsync(string ownerId, Guid id, string? title = null, string? notes = null,
                              DateTimeOffset? dueAtLocal = null, TodoPriority? priority = null, bool? pinned = null)
        {
            var item = await _db.TodoItems.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId && x.DeletedUtc == null);
            if (item == null) return false;
            if (title != null) item.Title = title;
            if (notes != null) item.Notes = notes;
            if (dueAtLocal.HasValue) item.DueAtUtc = ToUtc(dueAtLocal);
            if (priority.HasValue) item.Priority = priority.Value;
            if (pinned.HasValue && item.IsPinned != pinned.Value)
            {
                item.IsPinned = pinned.Value;
                await _audit.LogAsync(pinned.Value ? "Todo.Pin" : "Todo.Unpin", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            }
            item.UpdatedUtc = DateTimeOffset.UtcNow;
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException("This task was modified by another session. Please refresh and try again.");
            }
            await _audit.LogAsync("Todo.Update", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            return true;
        }

        public async Task<bool> DeleteAsync(string ownerId, Guid id)
        {
            var item = await _db.TodoItems.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId && x.DeletedUtc == null);
            if (item == null) return false;
            if (item.Status == TodoStatus.Done)
            {
                item.DeletedUtc = DateTimeOffset.UtcNow;
                item.UpdatedUtc = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync();
            }
            else
            {
                _db.TodoItems.Remove(item);
                await _db.SaveChangesAsync();
            }
            await _audit.LogAsync("Todo.Delete", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            return true;
        }

        public async Task<int> ClearCompletedAsync(string ownerId)
        {
            var items = await _db.TodoItems
                .Where(x => x.OwnerId == ownerId && x.Status == TodoStatus.Done && x.DeletedUtc == null)
                .ToListAsync();
            if (items.Count == 0) return 0;
            var now = DateTimeOffset.UtcNow;
            foreach (var item in items)
            {
                item.DeletedUtc = now;
                item.UpdatedUtc = now;
            }
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Todo.ClearCompleted", userId: ownerId);
            return items.Count;
        }

        public async Task<bool> ReorderAsync(string ownerId, IList<Guid> orderedIds)
        {
            var items = await _db.TodoItems
                .Where(x => x.OwnerId == ownerId && orderedIds.Contains(x.Id) && x.Status == TodoStatus.Open && x.DeletedUtc == null)
                .ToListAsync();
            if (items.Count != orderedIds.Count) return false;
            for (int i = 0; i < orderedIds.Count; i++)
            {
                var id = orderedIds[i];
                var item = items.First(x => x.Id == id);
                item.OrderIndex = i;
                item.UpdatedUtc = DateTimeOffset.UtcNow;
            }
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Todo.Reorder", userId: ownerId);
            return true;
        }
    }
}
