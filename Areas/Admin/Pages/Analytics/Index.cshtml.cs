using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Areas.Admin.Pages.Analytics
{
    [Authorize(Roles="Admin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        public IndexModel(ApplicationDbContext db) => _db = db;

        public int TotalUsers { get; private set; }
        public int ActiveUsers { get; private set; }
        public int DisabledUsers { get; private set; }
        public List<(DateTime Date, int Count)> LoginsPerDay { get; private set; } = new();
        public List<(string UserName, DateTime? LastLogin, int Count)> TopUsers { get; private set; } = new();
        public int[] LoginsLast30Days { get; private set; } = Array.Empty<int>();

        public async Task OnGet()
        {
            var users = _db.Users.AsNoTracking();
            TotalUsers = await users.CountAsync();
            var now = DateTimeOffset.UtcNow;
            ActiveUsers = await users.CountAsync(u => !u.LockoutEnd.HasValue || u.LockoutEnd <= now);
            DisabledUsers = TotalUsers - ActiveUsers;

            // Include today in the 30 day window by starting 29 days ago
            var sinceUtc = DateTime.UtcNow.Date.AddDays(-29);
            var raw = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "LoginSuccess" && a.TimeUtc >= sinceUtc)
                .GroupBy(a => a.TimeUtc.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();
            var dict = raw.ToDictionary(x => x.Date, x => x.Count);
            var list = new List<(DateTime Date, int Count)>();
            var arr = new int[30];
            for (int i = 0; i < 30; i++)
            {
                var d = sinceUtc.AddDays(i);
                dict.TryGetValue(d, out var c);
                list.Add((IstClock.ToIst(d), c));
                arr[i] = c;
            }
            LoginsPerDay = list;
            LoginsLast30Days = arr;

            TopUsers = await users
                .OrderByDescending(u => u.LoginCount)
                .Take(10)
                .Select(u => new { u.UserName, u.LastLoginUtc, u.LoginCount })
                .AsNoTracking()
                .ToListAsync()
                .ContinueWith(t => t.Result.Select(x => (x.UserName!, IstClock.ToIst(x.LastLoginUtc), x.LoginCount)).ToList());
        }
    }
}
