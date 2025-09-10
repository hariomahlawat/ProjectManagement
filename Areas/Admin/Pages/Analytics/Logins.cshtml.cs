using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using ProjectManagement.Models;
using ProjectManagement.Services;
using Microsoft.EntityFrameworkCore;

namespace ProjectManagement.Areas.Admin.Pages.Analytics
{
    [Authorize(Roles = "Admin")]
    public class LoginsModel : PageModel
    {
        private readonly ILoginAnalyticsService _svc;
        private readonly UserManager<ApplicationUser> _users;

        public LoginsModel(ILoginAnalyticsService svc, UserManager<ApplicationUser> users)
        {
            _svc = svc;
            _users = users;
        }

        public IList<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

        public async Task OnGetAsync()
        {
            Users = await _users.Users.OrderBy(u => u.UserName).ToListAsync();
        }

        public async Task<IActionResult> OnGetDataAsync(int days = 30, bool weekendOdd = false, string? user = null)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            var workStart = new TimeSpan(8, 0, 0);
            var workEnd = new TimeSpan(18, 0, 0);
            var dto = await _svc.GetAsync(days, weekendOdd, tz, workStart, workEnd, user);
            var payload = new
            {
                tz = dto.TimeZone,
                workStartMin = dto.WorkStartMin,
                workEndMin = dto.WorkEndMin,
                p50Min = dto.P50Min,
                p90Min = dto.P90Min,
                points = dto.Points.Select(p => new
                {
                    t = p.Local,
                    m = p.MinutesOfDay,
                    odd = p.IsOdd,
                    reason = p.Reason,
                    user = p.UserId
                })
            };
            return new JsonResult(payload);
        }

        public async Task<IActionResult> OnGetExportCsvAsync(int days = 30, bool weekendOdd = false, string? user = null)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            var workStart = new TimeSpan(8, 0, 0);
            var workEnd = new TimeSpan(18, 0, 0);
            var dto = await _svc.GetAsync(days, weekendOdd, tz, workStart, workEnd, user);
            var sb = new StringBuilder();
            sb.AppendLine("When,User,MinutesOfDay,IsOdd,Reason");
            foreach (var p in dto.Points)
            {
                sb.AppendLine($"{p.Local:u},{p.UserId},{p.MinutesOfDay},{p.IsOdd},{p.Reason}");
            }
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "logins.csv");
        }
    }
}
