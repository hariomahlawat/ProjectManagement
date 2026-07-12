using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.Admin.Pages.Analytics
{
    [Authorize(Roles = "Admin")]
    public class LoginsModel : PageModel
    {
        private const int MaximumLookbackDays = 365;

        private readonly ILoginAnalyticsService _service;
        private readonly UserManager<ApplicationUser> _users;

        public LoginsModel(ILoginAnalyticsService service, UserManager<ApplicationUser> users)
        {
            _service = service;
            _users = users;
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

            var builder = new StringBuilder();
            SafeCsv.AppendRow(builder, "WhenIST", "UserId", "LoginName", "DisplayName", "MinutesOfDay", "IsOdd", "Reason");

            foreach (var point in dto.Points)
            {
                SafeCsv.AppendRow(
                    builder,
                    point.Local.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                    point.UserId,
                    point.LoginName,
                    point.DisplayName,
                    point.MinutesOfDay,
                    point.IsOdd,
                    point.Reason);
            }

            return File(
                SafeCsv.ToUtf8WithBom(builder.ToString()),
                "text/csv; charset=utf-8",
                "logins.csv");
        }
    }
}
