using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.Logs
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private const int MaximumExportRows = 100_000;

        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db)
        {
            _db = db;
        }

        [BindProperty(SupportsGet = true)]
        public string? Level { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Action { get; set; }

        [BindProperty(SupportsGet = true, Name = "User")]
        public string? UserName { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Ip { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Contains { get; set; }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? From { get; set; }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? To { get; set; }

        [BindProperty(SupportsGet = true)]
        public int PageNo { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 25;

        [BindProperty(SupportsGet = true)]
        public string Sort { get; set; } = "Time";

        [BindProperty(SupportsGet = true)]
        public string Dir { get; set; } = "desc";

        public int Total { get; private set; }

        public IReadOnlyList<LogRow> Rows { get; private set; } = Array.Empty<LogRow>();

        public IReadOnlyList<string> ActionOptions { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<string> SeriesLabels { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<int> SeriesCounts { get; private set; } = Array.Empty<int>();

        public class LogRow
        {
            public DateTime TimeUtc { get; set; }
            public string Level { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public string? UserName { get; set; }
            public string? Ip { get; set; }
            public string? Message { get; set; }
            public string? DataJson { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            PageNo = Math.Max(PageNo, 1);
            PageSize = PageSize is > 0 and <= 200 ? PageSize : 25;

            var filteredQuery = ComposeQuery(_db.AuditLogs.AsNoTracking());
            Total = await filteredQuery.CountAsync();

            Rows = await ApplySort(filteredQuery, Sort, Dir)
                .Skip((PageNo - 1) * PageSize)
                .Take(PageSize)
                .Select(log => new LogRow
                {
                    TimeUtc = log.TimeUtc,
                    Level = log.Level,
                    Action = log.Action,
                    UserName = log.UserName,
                    Ip = log.Ip,
                    Message = log.Message,
                    DataJson = log.DataJson
                })
                .ToListAsync();

            ActionOptions = await _db.AuditLogs
                .AsNoTracking()
                .Select(log => log.Action)
                .Distinct()
                .OrderBy(action => action)
                .Take(100)
                .ToListAsync();

            var seriesTimes = await filteredQuery
                .Select(log => log.TimeUtc)
                .ToListAsync();

            var perIstDay = seriesTimes
                .GroupBy(timeUtc => DateOnly.FromDateTime(IstClock.ToIst(timeUtc)))
                .OrderBy(group => group.Key)
                .Select(group => new { Day = group.Key, Count = group.Count() })
                .ToList();

            SeriesLabels = perIstDay
                .Select(item => item.Day.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))
                .ToList();
            SeriesCounts = perIstDay.Select(item => item.Count).ToList();

            return Page();
        }

        public async Task<FileResult> OnGetExportCsvAsync()
        {
            var rows = await ApplySort(ComposeQuery(_db.AuditLogs.AsNoTracking()), "Time", "desc")
                .Take(MaximumExportRows)
                .ToListAsync();

            var builder = new StringBuilder();
            SafeCsv.AppendRow(builder, "TimeIST", "Level", "Action", "User", "IP", "Message", "DataJson");

            foreach (var row in rows)
            {
                var timeIst = IstClock.ToIst(row.TimeUtc)
                    .ToString("dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);

                SafeCsv.AppendRow(
                    builder,
                    timeIst,
                    row.Level,
                    row.Action,
                    row.UserName,
                    row.Ip,
                    row.Message,
                    row.DataJson);
            }

            var fileName = $"logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(
                SafeCsv.ToUtf8WithBom(builder.ToString()),
                "text/csv; charset=utf-8",
                fileName);
        }

        private IQueryable<AuditLog> ComposeQuery(IQueryable<AuditLog> query)
        {
            if (!string.IsNullOrWhiteSpace(Level))
            {
                query = query.Where(log => log.Level == Level);
            }

            if (!string.IsNullOrWhiteSpace(Action))
            {
                query = query.Where(log => log.Action == Action);
            }

            if (!string.IsNullOrWhiteSpace(UserName))
            {
                var userTerm = UserName.Trim().ToLowerInvariant();
                query = query.Where(log =>
                    log.UserName != null && log.UserName.ToLower().Contains(userTerm));
            }

            if (!string.IsNullOrWhiteSpace(Ip))
            {
                var ipTerm = Ip.Trim().ToLowerInvariant();
                query = query.Where(log => log.Ip != null && log.Ip.ToLower().Contains(ipTerm));
            }

            if (!string.IsNullOrWhiteSpace(Contains))
            {
                var contentTerm = Contains.Trim().ToLowerInvariant();
                query = query.Where(log =>
                    (log.Message != null && log.Message.ToLower().Contains(contentTerm))
                    || (log.DataJson != null && log.DataJson.ToLower().Contains(contentTerm)));
            }

            if (From.HasValue)
            {
                var fromDate = DateOnly.FromDateTime(From.Value);
                var fromUtc = IstClock.StartOfDayIstToUtc(fromDate).UtcDateTime;
                query = query.Where(log => log.TimeUtc >= fromUtc);
            }

            if (To.HasValue)
            {
                var toDate = DateOnly.FromDateTime(To.Value);
                var toUtcExclusive = IstClock.ExclusiveEndOfDayIstToUtc(toDate).UtcDateTime;
                query = query.Where(log => log.TimeUtc < toUtcExclusive);
            }

            return query;
        }

        private static IQueryable<AuditLog> ApplySort(IQueryable<AuditLog> query, string sort, string direction)
        {
            var ascending = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);

            return sort switch
            {
                "Level" => ascending
                    ? query.OrderBy(log => log.Level).ThenBy(log => log.TimeUtc)
                    : query.OrderByDescending(log => log.Level).ThenByDescending(log => log.TimeUtc),

                "Action" => ascending
                    ? query.OrderBy(log => log.Action).ThenBy(log => log.TimeUtc)
                    : query.OrderByDescending(log => log.Action).ThenByDescending(log => log.TimeUtc),

                "User" => ascending
                    ? query.OrderBy(log => log.UserName).ThenBy(log => log.TimeUtc)
                    : query.OrderByDescending(log => log.UserName).ThenByDescending(log => log.TimeUtc),

                "Ip" => ascending
                    ? query.OrderBy(log => log.Ip).ThenBy(log => log.TimeUtc)
                    : query.OrderByDescending(log => log.Ip).ThenByDescending(log => log.TimeUtc),

                _ => ascending
                    ? query.OrderBy(log => log.TimeUtc)
                    : query.OrderByDescending(log => log.TimeUtc)
            };
        }
    }
}
