using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;

namespace ProjectManagement.Areas.Admin.Pages.Logs
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        // ---------- Filters (GET-bound) ----------
        [BindProperty(SupportsGet = true)] public string? Level { get; set; }
        [BindProperty(SupportsGet = true)] public string? Action { get; set; }
        [BindProperty(SupportsGet = true)] public string? User { get; set; }
        [BindProperty(SupportsGet = true)] public string? Ip { get; set; }
        [BindProperty(SupportsGet = true)] public string? Contains { get; set; }
        [BindProperty(SupportsGet = true)] [DataType(DataType.Date)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] [DataType(DataType.Date)] public DateTime? To { get; set; }

        // ---------- Paging & sorting ----------
        [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25; // cap in OnGet
        [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "Time";
        [BindProperty(SupportsGet = true)] public string Dir { get; set; } = "desc"; // asc|desc

        // ---------- Results ----------
        public int Total { get; private set; }
        public IReadOnlyList<LogRow> Rows { get; private set; } = Array.Empty<LogRow>();

        // For UI helpers
        public IReadOnlyList<string> ActionOptions { get; private set; } = Array.Empty<string>();

        // Daily counts for the filtered range
        public IReadOnlyList<string> SeriesLabels { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<int> SeriesCounts { get; private set; } = Array.Empty<int>();

        public class LogRow
        {
            public DateTime TimeUtc { get; set; }
            public string Level { get; set; } = "";
            public string Action { get; set; } = "";
            public string? UserName { get; set; }
            public string? Ip { get; set; }
            public string? Message { get; set; }
            public string? DataJson { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (PageNo < 1) PageNo = 1;
            if (PageSize <= 0 || PageSize > 200) PageSize = 25; // sensible cap

            var q = ComposeQuery(_db.AuditLogs.AsNoTracking());

            Total = await q.CountAsync();

            q = ApplySort(q, Sort, Dir);

            Rows = await q
                .Skip((PageNo - 1) * PageSize)
                .Take(PageSize)
                .Select(x => new LogRow
                {
                    TimeUtc = x.TimeUtc,
                    Level = x.Level,
                    Action = x.Action,
                    UserName = x.UserName,
                    Ip = x.Ip,
                    Message = x.Message,
                    DataJson = x.DataJson
                })
                .ToListAsync();

            // Populate Action datalist with a small distinct set
            ActionOptions = await _db.AuditLogs.AsNoTracking()
                .Select(a => a.Action).Distinct().OrderBy(a => a).Take(50).ToListAsync();

            // Daily count series for the filtered range
            var perDay = await ComposeQuery(_db.AuditLogs.AsNoTracking())
                .GroupBy(x => x.TimeUtc.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .OrderBy(x => x.Day)
                .ToListAsync();

            SeriesLabels = perDay.Select(d => d.Day.ToString("yyyy-MM-dd")).ToList();
            SeriesCounts = perDay.Select(d => d.Count).ToList();

            return Page();
        }

        // CSV export for current filter
        public async Task<FileResult> OnGetExportCsvAsync()
        {
            var q = ApplySort(ComposeQuery(_db.AuditLogs.AsNoTracking()), "Time", "desc");

            // Hard cap to avoid accidental huge dumps
            var rows = await q.Take(100_000).ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("TimeIST,Level,Action,User,IP,Message,DataJson");

            foreach (var x in rows)
            {
                var tIst = IstClock.ToIst(x.TimeUtc).ToString("yyyy-MM-dd HH:mm:ss");
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(tIst), Csv(x.Level), Csv(x.Action), Csv(x.UserName), Csv(x.Ip),
                    Csv(x.Message), Csv(x.DataJson)
                }));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // ---------- Helpers ----------

        private IQueryable<AuditLog> ComposeQuery(IQueryable<AuditLog> q)
        {
            if (!string.IsNullOrWhiteSpace(Level)) q = q.Where(x => x.Level == Level);
            if (!string.IsNullOrWhiteSpace(Action)) q = q.Where(x => x.Action == Action);
            if (!string.IsNullOrWhiteSpace(User)) q = q.Where(x => x.UserName != null && x.UserName.Contains(User));
            if (!string.IsNullOrWhiteSpace(Ip)) q = q.Where(x => x.Ip != null && x.Ip.Contains(Ip));

            if (!string.IsNullOrWhiteSpace(Contains))
            {
                // Postgres: ILIKE for case-insensitive contains
                // Falls back to Contains if provider does not support ILike
                if (EF.Functions.GetType().GetMethod("ILike") != null)
                {
                    q = q.Where(x =>
                        (x.Message != null && EF.Functions.ILike(x.Message, $"%{Contains}%")) ||
                        (x.DataJson != null && EF.Functions.ILike(x.DataJson, $"%{Contains}%")));
                }
                else
                {
                    var needle = Contains.ToLowerInvariant();
                    q = q.Where(x =>
                        (x.Message != null && x.Message.ToLower().Contains(needle)) ||
                        (x.DataJson != null && x.DataJson.ToLower().Contains(needle)));
                }
            }

            if (From.HasValue) q = q.Where(x => x.TimeUtc >= From.Value);
            if (To.HasValue) q = q.Where(x => x.TimeUtc <= To.Value.AddDays(1).AddTicks(-1)); // inclusive day

            return q;
        }

        private static IQueryable<AuditLog> ApplySort(IQueryable<AuditLog> q, string sort, string dir)
        {
            bool asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);

            return sort switch
            {
                "Level" => asc ? q.OrderBy(x => x.Level).ThenBy(x => x.TimeUtc)
                               : q.OrderByDescending(x => x.Level).ThenByDescending(x => x.TimeUtc),

                "Action" => asc ? q.OrderBy(x => x.Action).ThenBy(x => x.TimeUtc)
                                : q.OrderByDescending(x => x.Action).ThenByDescending(x => x.TimeUtc),

                "User" => asc ? q.OrderBy(x => x.UserName).ThenBy(x => x.TimeUtc)
                              : q.OrderByDescending(x => x.UserName).ThenByDescending(x => x.TimeUtc),

                "Ip" => asc ? q.OrderBy(x => x.Ip).ThenBy(x => x.TimeUtc)
                            : q.OrderByDescending(x => x.Ip).ThenByDescending(x => x.TimeUtc),

                _ => asc ? q.OrderBy(x => x.TimeUtc) : q.OrderByDescending(x => x.TimeUtc)
            };
        }

        private static string Csv(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Escape double quotes and wrap
            var t = s.Replace("\"", "\"\"");
            return $"\"{t}\"";
        }
    }
}

