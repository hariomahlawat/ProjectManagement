using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Helpers;

namespace ProjectManagement.Pages.Tasks
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ITodoService _todo;
        private readonly UserManager<ApplicationUser> _users;
        private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

        public IndexModel(ApplicationDbContext db, ITodoService todo, UserManager<ApplicationUser> users)
        {
            _db = db; _todo = todo; _users = users;
        }

        public record Row(Guid Id, string Title, TodoPriority Priority, bool IsPinned,
                          TodoStatus Status, DateTimeOffset CreatedUtc, DateTimeOffset? DueAtUtc, DateTimeOffset? CompletedUtc);
        public record Group(string Title, Row[] Items);
        public Group[] Groups { get; set; } = Array.Empty<Group>();

        [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "all";   // all | today | upcoming | completed
        [BindProperty(SupportsGet = true)] public string? Q { get; set; }
        [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 50;

        public int TotalItems { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);

        public async Task OnGetAsync()
        {
            var uid = _users.GetUserId(User);
            var q = _db.TodoItems.AsNoTracking()
                .Where(x => x.OwnerId == uid && x.DeletedUtc == null);

            if (!string.IsNullOrWhiteSpace(Q))
            {
                var s = Q.Trim();
                q = q.Where(x => EF.Functions.ILike(x.Title, $"%{s}%"));
            }

            var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
            var startTodayLocal = nowLocal.Date;
            var endTodayLocal = startTodayLocal.AddDays(1);
            var startTodayUtc = TimeZoneInfo.ConvertTime(new DateTimeOffset(startTodayLocal, nowLocal.Offset), TimeZoneInfo.Utc);
            var endTodayUtc = TimeZoneInfo.ConvertTime(new DateTimeOffset(endTodayLocal, nowLocal.Offset), TimeZoneInfo.Utc);

            // Filter tab
            q = Tab switch
            {
                "today"     => q.Where(x => x.Status == TodoStatus.Open && x.DueAtUtc >= startTodayUtc && x.DueAtUtc < endTodayUtc),
                "upcoming"  => q.Where(x => x.Status == TodoStatus.Open && x.DueAtUtc >= endTodayUtc),
                "completed" => q.Where(x => x.Status == TodoStatus.Done),
                _           => q.Where(x => x.Status == TodoStatus.Open)
            };

            // Order for stable grouping: pinned first, then due, then orderindex
            var rows = await q
                .OrderByDescending(x => x.Status == TodoStatus.Open && x.IsPinned)
                .ThenBy(x => x.Status == TodoStatus.Open && x.DueAtUtc == null) // nulls last for open
                .ThenBy(x => x.DueAtUtc)
                .ThenBy(x => x.OrderIndex)
                .ThenBy(x => x.CreatedUtc)
                .Select(x => new Row(x.Id, x.Title, x.Priority, x.IsPinned, x.Status, x.CreatedUtc, x.DueAtUtc, x.CompletedUtc))
                .ToListAsync();

            // Group client-side (fast enough for a single user’s page)
            var overdue   = new List<Row>();
            var today     = new List<Row>();
            var upcoming  = new List<Row>();
            var completed = new List<Row>();

            foreach (var r in rows)
            {
                if (r.Status == TodoStatus.Done) { completed.Add(r); continue; }
                if (r.DueAtUtc is null) { upcoming.Add(r); continue; }

                var dueLocal = TimeZoneInfo.ConvertTime(r.DueAtUtc.Value, Ist);
                if      (dueLocal < nowLocal) overdue.Add(r);
                else if (dueLocal < endTodayLocal) today.Add(r);
                else                              upcoming.Add(r);
            }

            // Simple paging on the whole list (keeps code light). Keep Completed unpaged unless Tab=completed.
            var list = Tab == "completed" ? completed : overdue.Concat(today).Concat(upcoming).ToList();
            TotalItems = list.Count;
            list = list.Skip((Page - 1) * PageSize).Take(PageSize).ToList();

            var g = new List<Group>();
            if (Tab == "completed")
            {
                if (list.Count > 0) g.Add(new Group("Completed", list.ToArray()));
            }
            else
            {
                var pagedOverdue = list.Where(r => overdue.Contains(r)).ToArray();
                var pagedToday = list.Where(r => today.Contains(r)).ToArray();
                var pagedUpcoming = list.Where(r => upcoming.Contains(r)).ToArray();

                if (pagedOverdue.Length  > 0) g.Add(new Group("Overdue",  pagedOverdue));
                if (pagedToday.Length    > 0) g.Add(new Group("Today",    pagedToday));
                if (pagedUpcoming.Length > 0) g.Add(new Group("Upcoming", pagedUpcoming));
                if (g.Count == 0) g.Add(new Group("Open", Array.Empty<Row>()));
            }
            Groups = g.ToArray();
        }

        // Actions
        public async Task<IActionResult> OnPostAddAsync(string NewTitle)
        {
            var uid = _users.GetUserId(User);
            if (string.IsNullOrWhiteSpace(NewTitle) || uid == null) return Back();
            TodoQuickParser.Parse(NewTitle, out var clean, out var dueLocal, out var prio);
            clean = clean.Trim();
            if (string.IsNullOrEmpty(clean))
            {
                TempData["Error"] = "Task title cannot be empty.";
                return Back();
            }
            try
            {
                await _todo.CreateAsync(uid, clean, dueLocal, prio);
                TempData["ToastMessage"] = "Task added.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostToggleAsync(Guid id, bool done)
        {
            var uid = _users.GetUserId(User);
            try
            {
                await _todo.ToggleDoneAsync(uid!, id, done);
                if (done) TempData["UndoId"] = id.ToString();
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostUndoAsync(Guid id)
        {
            var uid = _users.GetUserId(User);
            try
            {
                await _todo.ToggleDoneAsync(uid!, id, false);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostEditAsync(Guid id, string? title, string? priority, DateTimeOffset? dueLocal, bool? pin)
        {
            var uid = _users.GetUserId(User);
            TodoPriority? prio = null;
            if (!string.IsNullOrEmpty(priority) && Enum.TryParse<TodoPriority>(priority, out var p)) prio = p;
            try
            {
                await _todo.EditAsync(uid!, id,
                    title: string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                    dueAtLocal: dueLocal,
                    priority: prio,
                    pinned: pin);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostSnoozeAsync(Guid id, string preset)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();

            DateTimeOffset? dueLocal = preset switch
            {
                "today_pm" => NextOccurrenceTodayOrTomorrow(18, 0),
                "tom_am"   => NextOccurrenceTodayOrTomorrow(10, 0).AddDays(1),
                "next_mon" => NextMondayAt(10, 0),
                "clear"    => null,
                _          => null
            };

            try
            {
                await _todo.EditAsync(uid, id, dueAtLocal: dueLocal);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostReorderAsync([FromForm] Guid[] ids)
        {
            var uid = _users.GetUserId(User);
            if (uid is null || ids is null || ids.Length == 0) return new OkResult();
            try
            {
                await _todo.ReorderAsync(uid, ids);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return new OkResult();
        }

        public async Task<IActionResult> OnPostBulkDoneAsync([FromForm] Guid[] ids)
        {
            var uid = _users.GetUserId(User);
            if (uid == null || ids == null || ids.Length == 0) return Back();
            try
            {
                await _todo.MarkDoneAsync(uid, ids);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostBulkDeleteAsync([FromForm] Guid[] ids)
        {
            var uid = _users.GetUserId(User);
            if (uid == null || ids == null || ids.Length == 0) return Back();
            try
            {
                await _todo.DeleteManyAsync(uid, ids);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostBulkPinAsync([FromForm] Guid[] ids, bool pin)
        {
            var uid = _users.GetUserId(User);
            if (uid == null || ids == null || ids.Length == 0) return Back();
            try
            {
                foreach (var id in ids)
                {
                    await _todo.EditAsync(uid, id, pinned: pin);
                }
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostDeleteAsync(Guid id)
        {
            var uid = _users.GetUserId(User);
            try
            {
                await _todo.DeleteAsync(uid!, id);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostClearCompletedAsync()
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Back();
            try
            {
                await _todo.ClearCompletedAsync(uid);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        public async Task<IActionResult> OnPostPinAsync(Guid id, bool pin)
        {
            var uid = _users.GetUserId(User);
            try
            {
                await _todo.EditAsync(uid!, id, pinned: pin);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return Back();
        }

        private IActionResult Back() => RedirectToPage("Index", new { Tab, Q, Page, PageSize });

        private static DateTimeOffset NextOccurrenceTodayOrTomorrow(int h, int m)
        {
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
            var candidate = new DateTimeOffset(nowIst.Year, nowIst.Month, nowIst.Day, h, m, 0, nowIst.Offset);
            if (candidate <= nowIst.AddMinutes(1)) candidate = candidate.AddDays(1);
            return candidate;
        }

        private static DateTimeOffset NextMondayAt(int h, int m)
        {
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
            int daysToMon = ((int)DayOfWeek.Monday - (int)nowIst.DayOfWeek + 7) % 7;
            if (daysToMon == 0) daysToMon = 7;
            var next = nowIst.Date.AddDays(daysToMon).AddHours(h).AddMinutes(m);
            return new DateTimeOffset(next, nowIst.Offset);
        }
    }
}

