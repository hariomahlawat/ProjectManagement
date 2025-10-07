using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Services
{
    public class TodoService : ITodoService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuditService _audit;
        private readonly IClock _clock;
        private static readonly TimeZoneInfo Ist = IstClock.TimeZone;
        private readonly int _maxOpenTasks;

        public TodoService(ApplicationDbContext db, IAuditService audit, IClock clock, IOptions<TodoOptions> options)
        {
            _db = db;
            _audit = audit;
            _clock = clock;
            _maxOpenTasks = options.Value.MaxOpenTasks;
        }

        private static DateTimeOffset? ToUtc(DateTimeOffset? localIst)
        {
            if (localIst is null) return null;
            var l = localIst.Value;
            var hasTime = l.TimeOfDay != TimeSpan.Zero;
            var stamp = hasTime
                ? new DateTimeOffset(l.Year, l.Month, l.Day, l.Hour, l.Minute, l.Second, Ist.GetUtcOffset(l))
                : new DateTimeOffset(l.Year, l.Month, l.Day, 23, 59, 59, Ist.GetUtcOffset(l)).AddTicks(9999999);
            return TimeZoneInfo.ConvertTime(stamp, TimeZoneInfo.Utc);
        }

        public async Task<TodoWidgetResult> GetWidgetAsync(string ownerId, int take = 20)
        {
            var nowUtc = _clock.UtcNow;
            var nowIst = TimeZoneInfo.ConvertTime(nowUtc, Ist);
            var startOfTodayIst = nowIst.Date;
            var startOfTomorrowIst = startOfTodayIst.AddDays(1);
            var startOfTomorrowUtc = TimeZoneInfo.ConvertTime(new DateTimeOffset(startOfTomorrowIst, nowIst.Offset), TimeZoneInfo.Utc);
            var startOfNextSevenUtc = TimeZoneInfo.ConvertTime(new DateTimeOffset(startOfTomorrowIst.AddDays(7), nowIst.Offset), TimeZoneInfo.Utc);
            var completedWindowStartUtc = nowUtc.AddDays(-14);

            var baseQuery = _db.TodoItems.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.Status == TodoStatus.Open && x.DeletedUtc == null);

            var dueQuery = baseQuery.Where(x => x.DueAtUtc != null);
            var overdueCount = await dueQuery.CountAsync(t => t.DueAtUtc < nowUtc);
            var dueTodayCount = await dueQuery.CountAsync(t => t.DueAtUtc >= nowUtc && t.DueAtUtc < startOfTomorrowUtc);
            var nextSevenCount = await dueQuery.CountAsync(t => t.DueAtUtc >= startOfTomorrowUtc && t.DueAtUtc < startOfNextSevenUtc);

            var recentCompletions = await _db.TodoItems.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.CompletedUtc != null && x.CompletedUtc >= completedWindowStartUtc && x.DeletedUtc == null)
                .Select(x => new { x.CompletedUtc, x.DueAtUtc })
                .ToListAsync();

            var completedCount = recentCompletions.Count;
            var onTimeCount = recentCompletions.Count(x => x.DueAtUtc == null || x.CompletedUtc <= x.DueAtUtc);
            var onTimePercent = completedCount == 0 ? 0 : (double)onTimeCount / completedCount * 100;

            var items = await baseQuery
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
                DueTodayCount = dueTodayCount,
                Next7DaysCount = nextSevenCount,
                OnTimePercent = onTimePercent
            };
        }

        public async Task<TodoItem> CreateAsync(string ownerId, string title, DateTimeOffset? dueAtLocal = null,
                                   TodoPriority priority = TodoPriority.Normal, bool pinned = false)
        {
            var utcDue = ToUtc(dueAtLocal);
            if (_maxOpenTasks > 0)
            {
                var openCount = await _db.TodoItems
                    .CountAsync(x => x.OwnerId == ownerId && x.Status == TodoStatus.Open && x.DeletedUtc == null);
                if (openCount >= _maxOpenTasks)
                {
                    throw new InvalidOperationException(
                        $"You can only keep {_maxOpenTasks} open tasks at a time. Complete or delete some before adding more.");
                }
            }
            var last = await _db.TodoItems.Where(x => x.OwnerId == ownerId && x.DeletedUtc == null).MaxAsync(x => (int?)x.OrderIndex) ?? -1;
            var item = new TodoItem
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                Title = title,
                DueAtUtc = utcDue,
                Priority = priority,
                IsPinned = pinned,
                Status = TodoStatus.Open,
                OrderIndex = last + 1,
                CreatedUtc = _clock.UtcNow,
                UpdatedUtc = _clock.UtcNow
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
                item.CompletedUtc = _clock.UtcNow;
                await _audit.LogAsync("Todo.Done", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            }
            else if (!done && item.Status == TodoStatus.Done)
            {
                item.Status = TodoStatus.Open;
                item.CompletedUtc = null;
                await _audit.LogAsync("Todo.Undone", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            }
            item.UpdatedUtc = _clock.UtcNow;
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

        public async Task<bool> EditAsync(string ownerId, Guid id, string? title = null,
                              DateTimeOffset? dueAtLocal = null, TodoPriority? priority = null, bool? pinned = null)
        {
            var item = await _db.TodoItems.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId && x.DeletedUtc == null);
            if (item == null) return false;
            if (title != null) item.Title = title;
            if (dueAtLocal.HasValue) item.DueAtUtc = ToUtc(dueAtLocal);
            if (priority.HasValue) item.Priority = priority.Value;
            if (pinned.HasValue && item.IsPinned != pinned.Value)
            {
                item.IsPinned = pinned.Value;
                await _audit.LogAsync(pinned.Value ? "Todo.Pin" : "Todo.Unpin", userId: ownerId, data: new Dictionary<string, string?> { ["Id"] = id.ToString() });
            }
            item.UpdatedUtc = _clock.UtcNow;
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
                item.DeletedUtc = _clock.UtcNow;
                item.UpdatedUtc = _clock.UtcNow;
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
            var now = _clock.UtcNow;
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
                item.UpdatedUtc = _clock.UtcNow;
            }
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Todo.Reorder", userId: ownerId);
            return true;
        }

        public async Task MarkDoneAsync(string ownerId, IList<Guid> ids)
        {
            foreach (var id in ids)
            {
                await ToggleDoneAsync(ownerId, id, true);
            }
        }

        public async Task DeleteManyAsync(string ownerId, IList<Guid> ids)
        {
            foreach (var id in ids)
            {
                await DeleteAsync(ownerId, id);
            }
        }
    }
}
