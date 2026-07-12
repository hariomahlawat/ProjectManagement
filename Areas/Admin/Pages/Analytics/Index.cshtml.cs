using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Analytics
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;

        public IndexModel(ApplicationDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

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
            DisabledUsers = await users.CountAsync(user => user.IsDisabled || user.PendingDeletion);
            ActiveUsers = TotalUsers - DisabledUsers;

            var currentIstDate = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
            var firstIstDate = currentIstDate.AddDays(-29);
            var fromUtc = IstClock.StartOfDayIstToUtc(firstIstDate);
            var toUtcExclusive = IstClock.ExclusiveEndOfDayIstToUtc(currentIstDate);

            var loginTimes = await _db.AuthEvents
                .AsNoTracking()
                .Where(authEvent =>
                    authEvent.Event == AuthenticationEventNames.LoginSucceeded
                    && authEvent.WhenUtc >= fromUtc
                    && authEvent.WhenUtc < toUtcExclusive)
                .Select(authEvent => authEvent.WhenUtc)
                .ToListAsync();

            var countsByIstDate = loginTimes
                .GroupBy(timestamp => DateOnly.FromDateTime(IstClock.ToIst(timestamp).DateTime))
                .ToDictionary(group => group.Key, group => group.Count());

            var daily = new List<(DateTime Date, int Count)>(30);
            var values = new int[30];
            for (var index = 0; index < 30; index++)
            {
                var date = firstIstDate.AddDays(index);
                countsByIstDate.TryGetValue(date, out var count);
                daily.Add((date.ToDateTime(TimeOnly.MinValue), count));
                values[index] = count;
            }

            LoginsPerDay = daily;
            LoginsLast30Days = values;

            var topLoginStats = await _db.AuthEvents
                .AsNoTracking()
                .Where(authEvent => authEvent.Event == AuthenticationEventNames.LoginSucceeded)
                .GroupBy(authEvent => authEvent.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    Count = group.Count(),
                    LastLoginUtc = group.Max(authEvent => authEvent.WhenUtc)
                })
                .OrderByDescending(row => row.Count)
                .ThenByDescending(row => row.LastLoginUtc)
                .Take(10)
                .ToListAsync();

            var topUserIds = topLoginStats.Select(row => row.UserId).ToArray();
            var userNames = topUserIds.Length == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await _db.Users
                    .AsNoTracking()
                    .Where(user => topUserIds.Contains(user.Id))
                    .Select(user => new
                    {
                        user.Id,
                        Display = string.IsNullOrWhiteSpace(user.FullName)
                            ? (user.UserName ?? user.Email ?? "(deleted)")
                            : user.FullName
                    })
                    .ToDictionaryAsync(user => user.Id, user => user.Display, StringComparer.Ordinal);

            TopUsers = topLoginStats
                .Select(row =>
                {
                    var display = userNames.TryGetValue(row.UserId, out var value) ? value : "(deleted)";
                    return (display, (DateTime?)row.LastLoginUtc.UtcDateTime, row.Count);
                })
                .ToList();
        }
    }
}
