using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Infrastructure;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.Admin.Pages.Analytics
{
    [Authorize(Policy = AdminPolicies.SecurityView)]
    public class LoginsModel : PageModel
    {
        private const int MaximumLookbackDays = 365;

        private readonly ILoginAnalyticsService _service;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISafeCsvWriter _csv;

        public LoginsModel(
            ILoginAnalyticsService service,
            UserManager<ApplicationUser> users,
            ISafeCsvWriter csv)
        {
            _service = service;
            _users = users;
            _csv = csv;
        }

        public IList<ApplicationUser> Users { get; private set; } = new List<ApplicationUser>();

        public async Task OnGetAsync()
        {
            Users = await _users.Users
                .AsNoTracking()
                .Where(user => !user.IsDisabled && !user.PendingDeletion)
                .OrderBy(user => user.UserName)
                .ToListAsync();
        }

        public async Task<IActionResult> OnGetDataAsync(int days = 30, bool weekendOdd = false, string? user = null)
        {
            var safeDays = Math.Clamp(days, 1, MaximumLookbackDays);
            var timeZone = TimeZoneHelper.GetIst();
            var workStart = new TimeSpan(8, 0, 0);
            var workEnd = new TimeSpan(18, 0, 0);
            var dto = await _service.GetAsync(safeDays, weekendOdd, timeZone, workStart, workEnd, user);

            return new JsonResult(new
            {
                tz = dto.TimeZone,
                workStartMin = dto.WorkStartMin,
                workEndMin = dto.WorkEndMin,
                p50Min = dto.P50Min,
                p90Min = dto.P90Min,
                points = dto.Points.Select(point => new
                {
                    t = point.Local,
                    m = point.MinutesOfDay,
                    odd = point.IsOdd,
                    reason = point.Reason,
                    user = point.UserId,
                    userName = point.DisplayName,
                    loginName = point.LoginName
                })
            });
        }

        public async Task<IActionResult> OnGetExportCsvAsync(int days = 30, bool weekendOdd = false, string? user = null)
        {
            var safeDays = Math.Clamp(days, 1, MaximumLookbackDays);
            var timeZone = TimeZoneHelper.GetIst();
            var workStart = new TimeSpan(8, 0, 0);
            var workEnd = new TimeSpan(18, 0, 0);
            var dto = await _service.GetAsync(safeDays, weekendOdd, timeZone, workStart, workEnd, user);

            var bytes = _csv.Write(
                new[] { "WhenIST", "UserId", "LoginName", "DisplayName", "MinutesOfDay", "IsOdd", "Reason" },
                dto.Points.Select(point => (IReadOnlyList<object?>)new object?[]
                {
                    point.Local.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                    point.UserId,
                    point.LoginName,
                    point.DisplayName,
                    point.MinutesOfDay,
                    point.IsOdd,
                    point.Reason
                }));

            return File(bytes, "text/csv; charset=utf-8", "logins.csv");
        }
    }
}
