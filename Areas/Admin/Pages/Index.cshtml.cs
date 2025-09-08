using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ProjectManagement.Areas.Admin.Pages
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public IList<AdminCard> Cards { get; } = new List<AdminCard>
        {
            new("Manage Users", "/Users/Index", "Create, edit and disable users", "users"),
            new("Analytics", "/Analytics/Index", "Review platform analytics", "analytics"),
            new("Logs", "/Logs/Index", "Inspect application logs", "logs")
        };

        public MetricsDto Metrics { get; private set; } = new();
        public IList<AdminActionDto> RecentAdminActions { get; private set; } = new List<AdminActionDto>();

        public record MetricsDto(int TotalUsers, int DisabledUsers, int MustChangePwd)
        {
            public MetricsDto() : this(0, 0, 0) { }
        }

        public record AdminActionDto(string Level, string Message, string WhenLocal);

        public async Task OnGet()
        {
            var users = await _db.Users.AsNoTracking().ToListAsync();
            Metrics = new MetricsDto(users.Count, users.Count(u => u.IsDisabled), users.Count(u => u.MustChangePassword));

            RecentAdminActions = await _db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.TimeUtc)
                .Take(10)
                .Select(a => new AdminActionDto(a.Level, a.Message ?? a.Action, TimeFmt.ToIst(a.TimeUtc)))
                .ToListAsync();
        }
    }
}
