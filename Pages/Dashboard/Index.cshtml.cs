using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Dashboard.Components.ProjectPulse;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Dashboard;
using ProjectManagement.Helpers;
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;

namespace ProjectManagement.Pages.Dashboard
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ITodoService _todo;
        private readonly UserManager<ApplicationUser> _users;
        private readonly Data.ApplicationDbContext _db;
        private readonly ProjectPulseService _projectPulse;
        private static readonly TimeZoneInfo IST = IstClock.TimeZone;

        public IndexModel(ITodoService todo, UserManager<ApplicationUser> users, Data.ApplicationDbContext db, ProjectPulseService projectPulse)
        {
            _todo = todo;
            _users = users;
            _db = db;
            _projectPulse = projectPulse;
        }

        public TodoWidgetResult? TodoWidget { get; set; }
        public List<UpcomingEventVM> UpcomingEvents { get; set; } = new();
        public List<MyProjectsSection> MyProjectSections { get; private set; } = new();
        public bool HasMyProjects => MyProjectSections.Any(section => section.Items.Count > 0);
        public ProjectPulseVm? ProjectPulse { get; private set; }

        // SECTION: My Projects widget state
        public bool ShowMyProjectsWidget { get; private set; }
        public bool ShowEmptyMyProjectsMessage { get; private set; }
        // END SECTION

        public class UpcomingEventVM
        {
            public Guid? Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string When { get; set; } = string.Empty;
            public bool IsHoliday { get; set; }
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

            var upcoming = new List<(Guid? Id, string Title, DateTime StartUtc, DateTime EndUtc, bool IsAllDay, bool IsHoliday)>();

            var events = await _db.Events.AsNoTracking()
                .Where(e => !e.IsDeleted && e.StartUtc >= nowUtc && e.StartUtc < rangeEnd)
                .OrderBy(e => e.StartUtc)
                .Take(15)
                .Select(e => new { e.Id, e.Title, e.StartUtc, e.EndUtc, e.IsAllDay })
                .ToListAsync();
            foreach (var ev in events)
            {
                upcoming.Add((ev.Id, ev.Title, ev.StartUtc.UtcDateTime, ev.EndUtc.UtcDateTime, ev.IsAllDay, false));
            }

            var todayIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, IST));
            var windowEndIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(rangeEnd, IST));
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
                    var endUtc = CelebrationHelpers.ToLocalDateTime(nextOccurrence.AddDays(1)).UtcDateTime;
                    upcoming.Add((celebration.Id, title, startUtc, endUtc, true, false));
                }
            }

            var holidays = await _db.Holidays.AsNoTracking()
                .Where(h => h.Date >= todayIst && h.Date <= windowEndIst)
                .ToListAsync();

            foreach (var holiday in holidays)
            {
                var startLocal = DateTime.SpecifyKind(holiday.Date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
                var endLocal = DateTime.SpecifyKind(holiday.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, IST);
                var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, IST);
                upcoming.Add((null, $"Holiday: {holiday.Name}", startUtc, endUtc, true, true));
            }

            foreach (var item in upcoming
                .OrderBy(x => x.StartUtc)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .Take(10))
            {
                var startLocal = TimeZoneInfo.ConvertTimeFromUtc(item.StartUtc, IST);
                var endLocal = TimeZoneInfo.ConvertTimeFromUtc(item.EndUtc, IST);
                string when;
                if (item.IsAllDay)
                {
                    var startDate = DateOnly.FromDateTime(startLocal);
                    var inclusiveEndCandidate = endLocal.AddDays(-1);
                    if (inclusiveEndCandidate < startLocal)
                    {
                        inclusiveEndCandidate = startLocal;
                    }
                    var endDate = DateOnly.FromDateTime(inclusiveEndCandidate);
                    when = startDate == endDate
                        ? startDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                        : string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:dd MMM yyyy} – {1:dd MMM yyyy}",
                            startDate,
                            endDate);
                }
                else
                {
                    var startStr = startLocal.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
                    if (startLocal == endLocal)
                    {
                        when = startStr;
                    }
                    else if (startLocal.Date == endLocal.Date)
                    {
                        when = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} – {1:HH:mm}",
                            startStr,
                            endLocal);
                    }
                    else
                    {
                        when = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} – {1:dd MMM yyyy, HH:mm}",
                            startStr,
                            endLocal);
                    }
                }
                UpcomingEvents.Add(new UpcomingEventVM { Id = item.Id, Title = item.Title, When = when, IsHoliday = item.IsHoliday });
            }

            var isProjectOfficer = User.IsInRole(RoleNames.ProjectOfficer);
            var isHod = User.IsInRole(RoleNames.HoD);
            var isComdt = User.IsInRole(RoleNames.Comdt);
            var isMco = User.IsInRole(RoleNames.Mco);

            ShowMyProjectsWidget = isProjectOfficer || isHod || isComdt || isMco;
            ShowEmptyMyProjectsMessage = isProjectOfficer || isHod;

            if (uid != null && ShowMyProjectsWidget)
            {
                await LoadMyProjectsAsync(uid, isProjectOfficer, isHod, isComdt || isMco);
            }

            ProjectPulse = await _projectPulse.GetAsync();
        }

        private async Task LoadMyProjectsAsync(string userId, bool includeOfficerSection, bool includeHodSection, bool includeAllOngoingSection)
        {
            var sections = new List<MyProjectsSection>();

            if (includeOfficerSection)
            {
                var officerProjects = await OnlyOngoing(_db.Projects.AsNoTracking())
                    .Where(p => p.LeadPoUserId == userId)
                    .OrderBy(p => p.Name)
                    .Select(p => new ProjectAssignmentSummary
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Category = p.Category != null ? p.Category.Name : null,
                        CoverPhotoId = p.CoverPhotoId,
                        CoverPhotoVersion = p.CoverPhotoVersion
                    })
                    .ToListAsync();

                if (officerProjects.Count > 0)
                {
                    sections.Add(new MyProjectsSection
                    {
                        Title = "Project Officer",
                        Items = officerProjects.Select(CreateProjectItem).ToList()
                    });
                }
            }

            if (includeHodSection)
            {
                var hodProjects = await OnlyOngoing(_db.Projects.AsNoTracking())
                    .Where(p => p.HodUserId == userId)
                    .OrderBy(p => p.Name)
                    .Select(p => new ProjectAssignmentSummary
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Category = p.Category != null ? p.Category.Name : null,
                        CoverPhotoId = p.CoverPhotoId,
                        CoverPhotoVersion = p.CoverPhotoVersion
                    })
                    .ToListAsync();

                if (hodProjects.Count > 0)
                {
                    sections.Add(new MyProjectsSection
                    {
                        Title = "Head of Department",
                        Items = hodProjects.Select(CreateProjectItem).ToList()
                    });
                }
            }

            if (includeAllOngoingSection)
            {
                var allOngoingProjects = await OnlyOngoing(_db.Projects.AsNoTracking())
                    .OrderBy(p => p.Name)
                    .Select(p => new ProjectAssignmentSummary
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Category = p.Category != null ? p.Category.Name : null,
                        CoverPhotoId = p.CoverPhotoId,
                        CoverPhotoVersion = p.CoverPhotoVersion
                    })
                    .ToListAsync();

                if (allOngoingProjects.Count > 0)
                {
                    sections.Add(new MyProjectsSection
                    {
                        Title = "All ongoing projects",
                        Items = allOngoingProjects.Select(CreateProjectItem).ToList()
                    });
                }
            }

            MyProjectSections = sections;

            MyProjectItem CreateProjectItem(ProjectAssignmentSummary summary)
            {
                string? coverImageUrl = summary.CoverPhotoId.HasValue
                    ? Url.Page("/Projects/Photos/View", new
                    {
                        id = summary.Id,
                        photoId = summary.CoverPhotoId.Value,
                        size = "xs",
                        v = summary.CoverPhotoVersion
                    })
                    : null;

                return new MyProjectItem
                {
                    ProjectId = summary.Id,
                    Name = summary.Name,
                    Category = string.IsNullOrWhiteSpace(summary.Category) ? null : summary.Category,
                    CoverImageUrl = coverImageUrl
                };
            }
        }

        // SECTION: My Projects helpers
        private static IQueryable<Project> OnlyOngoing(IQueryable<Project> query)
        {
            return query.Where(p => !p.IsDeleted
                && !p.IsArchived
                && p.LifecycleStatus != ProjectLifecycleStatus.Completed
                && p.LifecycleStatus != ProjectLifecycleStatus.Cancelled);
        }
        // END SECTION

        public sealed class MyProjectsSection
        {
            public string Title { get; init; } = string.Empty;
            public List<MyProjectItem> Items { get; init; } = new();
        }

        public sealed class MyProjectItem
        {
            public int ProjectId { get; init; }
            public string Name { get; init; } = string.Empty;
            public string? Category { get; init; }
            public string? CoverImageUrl { get; init; }
        }

        private sealed class ProjectAssignmentSummary
        {
            public int Id { get; init; }
            public string Name { get; init; } = string.Empty;
            public string? Category { get; init; }
            public int? CoverPhotoId { get; init; }
            public int CoverPhotoVersion { get; init; }
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTitle))
                return RedirectToPage();

            var uid = _users.GetUserId(User);
            if (uid == null) return Unauthorized();

            TodoQuickParser.Parse(NewTitle, out var clean, out var dueLocal, out var prio);
            clean = clean.Trim();
            if (string.IsNullOrEmpty(clean))
            {
                TempData["Error"] = "Task title cannot be empty.";
                return RedirectToPage();
            }

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

            var nowIst = GetNowIst();

            DateTimeOffset? dueLocal = preset switch
            {
                "today_pm" => NextOccurrenceTodayOrTomorrow(18, 0),          // Today 6 PM or tomorrow if passed
                "tom_am"   => new DateTimeOffset(nowIst.Date.AddDays(1).AddHours(10), nowIst.Offset),
                "next_mon" => NextMondayAt(10, 0),
                "clear"    => null,
                _          => null
            };
            try
            {
                await _todo.EditAsync(uid, id, dueAtLocal: dueLocal, updateDueDate: true);
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToPage();
        }

        internal virtual DateTimeOffset GetNowIst() =>
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IST);

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
