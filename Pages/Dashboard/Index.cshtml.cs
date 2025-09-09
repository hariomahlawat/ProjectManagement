using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Helpers;
using System;
using System.Threading.Tasks;

namespace ProjectManagement.Pages.Dashboard
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ITodoService _todo;
        private readonly INoteService _notes;
        private readonly UserManager<ApplicationUser> _users;
        private static readonly TimeZoneInfo IST = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        public IndexModel(ITodoService todo, INoteService notes, UserManager<ApplicationUser> users)
        {
            _todo = todo;
            _notes = notes;
            _users = users;
        }

        public TodoWidgetResult? TodoWidget { get; set; }
        public IList<Note> Notes { get; set; } = new List<Note>();

        [BindProperty]
        public string? NewTitle { get; set; }
        [BindProperty]
        public string? NewNoteTitle { get; set; }

        public async Task OnGetAsync()
        {
            var uid = _users.GetUserId(User);
            if (uid != null)
            {
                TodoWidget = await _todo.GetWidgetAsync(uid, take: 20);
                Notes = await _notes.ListStandaloneAsync(uid);
            }
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTitle))
                return RedirectToPage();

            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();

            TodoQuickParser.Parse(NewTitle, out var clean, out var dueLocal, out var prio);

            try
            {
                await _todo.CreateAsync(uid, clean, dueLocal, prio);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostQuickNoteAddAsync()
        {
            if (string.IsNullOrWhiteSpace(NewNoteTitle))
                return RedirectToPage();
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            await _notes.CreateAsync(uid, null, NewNoteTitle.Trim(), null);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleAsync(Guid id, bool done)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            try
            {
                await _todo.ToggleDoneAsync(uid, id, done);
                if (done) TempData["UndoId"] = id.ToString();
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(Guid id)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            try
            {
                await _todo.DeleteAsync(uid, id);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostPinAsync(Guid id, bool pin)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            try
            {
                await _todo.EditAsync(uid, id, pinned: pin);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEditAsync(Guid id, string priority)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            TodoPriority prio = TodoPriority.Normal;
            Enum.TryParse(priority, out prio);
            try
            {
                await _todo.EditAsync(uid, id, priority: prio);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSnoozeAsync(Guid id, string preset)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();

            DateTimeOffset? dueLocal = preset switch
            {
                "today_pm" => NextOccurrenceTodayOrTomorrow(18, 0),          // Today 6 PM or tomorrow if passed
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
            return RedirectToPage();
        }

        private static DateTimeOffset NextOccurrenceTodayOrTomorrow(int h, int m)
        {
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IST);
            var candidate = new DateTimeOffset(nowIst.Year, nowIst.Month, nowIst.Day, h, m, 0, nowIst.Offset);
            // If time already passed (with a tiny 1-minute grace), bump to next day
            if (candidate <= nowIst.AddMinutes(1)) candidate = candidate.AddDays(1);
            return candidate;
        }

        private static DateTimeOffset NextMondayAt(int h, int m)
        {
            var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
            int daysToMon = ((int)DayOfWeek.Monday - (int)nowIst.DayOfWeek + 7) % 7;
            if (daysToMon == 0) daysToMon = 7;
            var next = nowIst.Date.AddDays(daysToMon).AddHours(h).AddMinutes(m);
            return new DateTimeOffset(next, nowIst.Offset);
        }
    }
}
