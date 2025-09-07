using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.Admin.Pages.Logs
{
    [Authorize(Roles="Admin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)] public string? Level { get; set; }
        [BindProperty(SupportsGet = true)] public string? Action { get; set; }
        [BindProperty(SupportsGet = true)] public new string? User { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;

        public int Total { get; private set; }
        public int PageSize { get; } = 50;
        public List<LogRow> Rows { get; private set; } = new();

        public class LogRow
        {
            public DateTime Time { get; set; }
            public string Level { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public string? UserName { get; set; }
            public string? Ip { get; set; }
            public string? Message { get; set; }
        }

        public async Task OnGet()
        {
            var q = _db.AuditLogs.AsNoTracking().OrderByDescending(x => x.TimeUtc).AsQueryable();
            if (!string.IsNullOrWhiteSpace(Level)) q = q.Where(x => x.Level == Level);
            if (!string.IsNullOrWhiteSpace(Action)) q = q.Where(x => x.Action == Action);
            if (!string.IsNullOrWhiteSpace(User)) q = q.Where(x => x.UserName!.Contains(User));
            if (From.HasValue) q = q.Where(x => x.TimeUtc >= From.Value);
            if (To.HasValue)   q = q.Where(x => x.TimeUtc <= To.Value);

            Total = await q.CountAsync();
            Rows = await q.Skip((PageNo-1)*PageSize).Take(PageSize)
                .Select(x => new LogRow {
                    Time = x.TimeUtc, Level = x.Level, Action = x.Action,
                    UserName = x.UserName, Ip = x.Ip, Message = x.Message
                }).ToListAsync();
        }
    }
}
