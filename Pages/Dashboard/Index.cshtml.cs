using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Models;
using ProjectManagement.Services;
using System;
using System.Threading.Tasks;

namespace ProjectManagement.Pages.Dashboard
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ITodoService _todo;
        private readonly UserManager<ApplicationUser> _users;

        public IndexModel(ITodoService todo, UserManager<ApplicationUser> users)
        {
            _todo = todo;
            _users = users;
        }

        public TodoWidgetResult? TodoWidget { get; set; }

        [BindProperty]
        public string? NewTitle { get; set; }

        public async Task OnGetAsync()
        {
            var uid = _users.GetUserId(User);
            if (uid != null)
            {
                TodoWidget = await _todo.GetWidgetAsync(uid, take: 20);
            }
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTitle))
                return RedirectToPage();

            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();

            await _todo.CreateAsync(uid, NewTitle.Trim());
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleAsync(Guid id)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            // Widget shows only Open items; toggling marks them done.
            await _todo.ToggleDoneAsync(uid, id, done: true);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(Guid id)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            await _todo.DeleteAsync(uid, id);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostPinAsync(Guid id, bool pin)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();
            await _todo.EditAsync(uid, id, pinned: pin);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSnoozeAsync(Guid id, string preset)
        {
            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();

            DateTimeOffset? dueLocal = preset switch
            {
                "today_pm" => TodayAt(18, 0),
                "tom_am" => TodayAt(10, 0).AddDays(1),
                "next_mon" => NextMondayAt(10, 0),
                "clear" => null,
                _ => null
            };
            await _todo.EditAsync(uid, id, dueAtLocal: dueLocal);
            return RedirectToPage();
        }

        private static DateTimeOffset TodayAt(int h, int m)
        {
            var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
            var dt = new DateTimeOffset(nowIst.Year, nowIst.Month, nowIst.Day, h, m, 0, nowIst.Offset);
            return dt;
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
