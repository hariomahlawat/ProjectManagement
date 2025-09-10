using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services
{
    public record LoginPoint(string UserId, DateTimeOffset Local, int MinutesOfDay, bool IsOdd, string Reason);
    public record LoginAnalyticsDto(
        string TimeZone, int WorkStartMin, int WorkEndMin, int P50Min, int P90Min,
        IReadOnlyList<LoginPoint> Points);

    public interface ILoginAnalyticsService
    {
        Task<LoginAnalyticsDto> GetAsync(int days, bool markWeekendOdd, TimeZoneInfo tz,
            TimeSpan workStart, TimeSpan workEnd, string? userId = null);
    }

    public class LoginAnalyticsService : ILoginAnalyticsService
    {
        private readonly ApplicationDbContext _db;
        public LoginAnalyticsService(ApplicationDbContext db) => _db = db;

        public async Task<LoginAnalyticsDto> GetAsync(int days, bool markWeekendOdd, TimeZoneInfo tz,
            TimeSpan workStart, TimeSpan workEnd, string? userId = null)
        {
            var toUtc = DateTimeOffset.UtcNow;
            var fromUtc = toUtc.AddDays(-days);

            var query = _db.AuthEvents
                .Where(e => e.Event == "LoginSucceeded" && e.WhenUtc >= fromUtc && e.WhenUtc <= toUtc);
            if (!string.IsNullOrWhiteSpace(userId))
                query = query.Where(e => e.UserId == userId);

            var rows = await query
                .Select(e => new { e.UserId, e.WhenUtc })
                .ToListAsync();

            var points = new List<LoginPoint>(rows.Count);
            var mins = new List<int>(rows.Count);

            foreach (var r in rows)
            {
                var local = TimeZoneInfo.ConvertTime(r.WhenUtc, tz);
                var tod = local.TimeOfDay;
                var m = (int)Math.Round(tod.TotalMinutes);
                var outsideHours = tod < workStart || tod > workEnd;
                var weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var isOdd = outsideHours || (markWeekendOdd && weekend);
                var reason = isOdd ? (outsideHours ? "Outside working hours" : "Weekend") : string.Empty;
                points.Add(new LoginPoint(r.UserId, local, m, isOdd, reason));
                mins.Add(m);
            }

            mins.Sort();
            int p50 = Percentile(mins, 50);
            int p90 = Percentile(mins, 90);

            return new LoginAnalyticsDto(
                tz.Id, (int)workStart.TotalMinutes, (int)workEnd.TotalMinutes, p50, p90, points);
        }

        private static int Percentile(IList<int> sortedMins, int p)
        {
            if (sortedMins.Count == 0) return 0;
            if (sortedMins.Count == 1) return sortedMins[0];
            var rank = (p / 100.0) * (sortedMins.Count - 1);
            var lo = (int)Math.Floor(rank);
            var hi = (int)Math.Ceiling(rank);
            if (lo == hi) return sortedMins[lo];
            var val = sortedMins[lo] + (sortedMins[hi] - sortedMins[lo]) * (rank - lo);
            return (int)Math.Round(val);
        }
    }
}
