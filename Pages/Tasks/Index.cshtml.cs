using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Pages.Tasks
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ITodoService _todo;
        private readonly UserManager<ApplicationUser> _users;
        private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        public IndexModel(ApplicationDbContext db, ITodoService todo, UserManager<ApplicationUser> users)
        {
            _db = db; _todo = todo; _users = users;
        }

        public record Row(Guid Id, string Title, TodoPriority Priority, bool IsPinned, TodoStatus Status, DateTimeOffset? DueAtUtc, DateTimeOffset? CompletedUtc, string? Notes);
        public Row[] Items { get; set; } = Array.Empty<Row>();
        [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "all"; // all | today | upcoming | completed
        [BindProperty(SupportsGet = true)] public string? Q { get; set; }
        [BindProperty(SupportsGet = true)] public int Page { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
        public int TotalItems { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);

        public async Task OnGetAsync()
        {
            var uid = _users.GetUserId(User);
            var q = _db.TodoItems.AsNoTracking().Where(x => x.OwnerId == uid && x.DeletedUtc == null);

            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
            var startTodayIst = new DateTimeOffset(nowIst.Date, nowIst.Offset);
            var endTodayIst = startTodayIst.AddDays(1).AddTicks(-1);
            var startTodayUtc = TimeZoneInfo.ConvertTime(startTodayIst, TimeZoneInfo.Utc);
            var endTodayUtc = TimeZoneInfo.ConvertTime(endTodayIst, TimeZoneInfo.Utc);

            if (!string.IsNullOrWhiteSpace(Q))
            {
                var qnorm = Q.Trim();
                q = q.Where(x => EF.Functions.Like(x.Title, $"%{qnorm}%") || (x.Notes != null && EF.Functions.Like(x.Notes, $"%{qnorm}%")));
            }

            Tab = Tab?.ToLowerInvariant() ?? "all";
            q = Tab switch
            {
                "today"     => q.Where(x => x.Status == TodoStatus.Open && x.DueAtUtc >= startTodayUtc && x.DueAtUtc <= endTodayUtc),
                "upcoming"  => q.Where(x => x.Status == TodoStatus.Open && x.DueAtUtc > endTodayUtc),
                "completed" => q.Where(x => x.Status == TodoStatus.Done),
                _           => q.Where(x => x.Status == TodoStatus.Open)
            };

            q = q
                .OrderByDescending(x => x.Status == TodoStatus.Open && x.IsPinned)
                .ThenBy(x => x.Status == TodoStatus.Open && x.DueAtUtc == null) // nulls last for open
                .ThenBy(x => x.DueAtUtc)
                .ThenBy(x => x.OrderIndex)
                .ThenBy(x => x.CreatedUtc);

            TotalItems = await q.CountAsync();
            Items = await q
                .Skip((Page - 1) * PageSize)
                .Take(PageSize)
                .Select(x => new Row(x.Id, x.Title, x.Priority, x.IsPinned, x.Status, x.DueAtUtc, x.CompletedUtc, x.Notes)).ToArrayAsync();
        }

        // Actions
        public async Task<IActionResult> OnPostAddAsync(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return RedirectToPage(new { Tab, Q });
            var uid = _users.GetUserId(User);
            try
            {
                await _todo.CreateAsync(uid!, title.Trim());
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage(new { Tab, Q, Page, PageSize });
        }

        public async Task<IActionResult> OnPostToggleAsync(Guid id, bool done)
        {
            var uid = _users.GetUserId(User);
            try
            {
                await _todo.ToggleDoneAsync(uid!, id, done);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage(new { Tab, Q, Page, PageSize });
        }

        public async Task<IActionResult> OnPostEditAsync(Guid id, string? title, string? priority, DateTimeOffset? dueLocal, bool? pin, string? notes)
        {
            var uid = _users.GetUserId(User);
            TodoPriority? prio = null;
            if (!string.IsNullOrEmpty(priority) && Enum.TryParse<TodoPriority>(priority, out var p)) prio = p;
            try
            {
                await _todo.EditAsync(uid!, id,
                    title: string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                    notes: notes,
                    dueAtLocal: dueLocal,
                    priority: prio,
                    pinned: pin);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage(new { Tab, Q, Page, PageSize });
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
            return RedirectToPage(new { Tab, Q, Page, PageSize });
        }

        public async Task<IActionResult> OnPostBatchDoneAsync([FromForm] Guid[] ids)
        {
            var uid = _users.GetUserId(User);
            if (uid == null || ids == null || ids.Length == 0) return RedirectToPage(new { Tab, Q, Page, PageSize });
            foreach (var id in ids)
            {
                try
                {
                    await _todo.ToggleDoneAsync(uid, id, true);
                }
                catch (InvalidOperationException ex)
                {
                    TempData["Error"] = ex.Message;
                    break;
                }
            }
            return RedirectToPage(new { Tab, Q, Page, PageSize });
        }

        public async Task<IActionResult> OnPostBatchDeleteAsync([FromForm] Guid[] ids)
        {
            var uid = _users.GetUserId(User);
            if (uid == null || ids == null || ids.Length == 0) return RedirectToPage(new { Tab, Q, Page, PageSize });
            foreach (var id in ids)
            {
                try
                {
                    await _todo.DeleteAsync(uid, id);
                }
                catch (InvalidOperationException ex)
                {
                    TempData["Error"] = ex.Message;
                    break;
                }
            }
            return RedirectToPage(new { Tab, Q, Page, PageSize });
        }
    }
}

