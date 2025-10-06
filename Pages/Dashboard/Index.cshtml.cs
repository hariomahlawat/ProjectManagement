using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Helpers;
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace ProjectManagement.Pages.Dashboard
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ITodoService _todo;
        private readonly UserManager<ApplicationUser> _users;
        private readonly Data.ApplicationDbContext _db;
        private static readonly TimeZoneInfo IST = IstClock.TimeZone;

        public IndexModel(ITodoService todo, UserManager<ApplicationUser> users, Data.ApplicationDbContext db)
        {
            _todo = todo;
            _users = users;
            _db = db;
        }

        public TodoWidgetResult? TodoWidget { get; set; }
        public List<UpcomingEventVM> UpcomingEvents { get; set; } = new();

        public class UpcomingEventVM
        {
            public Guid Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string When { get; set; } = string.Empty;
        }

        [BindProperty]
        public string? NewTitle { get; set; }

        public async Task OnGetAsync()
        {
            var uid = _users.GetUserId(User);
            if (uid != null)
            {
                TodoWidget = await _todo.GetWidgetAsync(uid, take: 20);
            }

            var nowUtc = DateTime.UtcNow;
            var rangeEnd = nowUtc.AddDays(30);

            var upcoming = new List<(Guid Id, string Title, DateTime StartUtc, bool IsAllDay)>();

            var events = await _db.Events.AsNoTracking()
                .Where(e => !e.IsDeleted && e.StartUtc >= nowUtc && e.StartUtc < rangeEnd)
                .OrderBy(e => e.StartUtc)
                .Take(15)
                .Select(e => new { e.Id, e.Title, e.StartUtc, e.IsAllDay })
                .ToListAsync();
            foreach (var ev in events)
            {
                upcoming.Add((ev.Id, ev.Title, ev.StartUtc.UtcDateTime, ev.IsAllDay));
            }

            var todayIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, IST));
            var celebrations = await _db.Celebrations.AsNoTracking()
                .Where(c => c.DeletedUtc == null)
                .ToListAsync();

            foreach (var celebration in celebrations)
            {
                var nextOccurrence = CelebrationHelpers.NextOccurrenceLocal(celebration, todayIst);

                var startLocal = CelebrationHelpers.ToLocalDateTime(nextOccurrence);
                var startUtc = startLocal.UtcDateTime;
                if (startUtc >= nowUtc && startUtc < rangeEnd)
                {
                    var titlePrefix = celebration.EventType switch
                    {
                        CelebrationType.Birthday => "Birthday",
                        CelebrationType.Anniversary => "Anniversary",
                        _ => celebration.EventType.ToString()
                    };
                    var title = $"{titlePrefix}: {CelebrationHelpers.DisplayName(celebration)}";
                    upcoming.Add((celebration.Id, title, startUtc, true));
                }
            }

            foreach (var item in upcoming
                .OrderBy(x => x.StartUtc)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Take(10))
            {
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(item.StartUtc, IST);
                var when = item.IsAllDay
                    ? startLocal.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                    : startLocal.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
                UpcomingEvents.Add(new UpcomingEventVM { Id = item.Id, Title = item.Title, When = when });
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
            var ist = IST;
            var nowIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ist);
            int daysToMon = ((int)DayOfWeek.Monday - (int)nowIst.DayOfWeek + 7) % 7;
            if (daysToMon == 0) daysToMon = 7;
            var next = nowIst.Date.AddDays(daysToMon).AddHours(h).AddMinutes(m);
            return new DateTimeOffset(next, nowIst.Offset);
        }
    }
}
