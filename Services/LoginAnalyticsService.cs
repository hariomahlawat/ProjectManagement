using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services
{
    public record LoginPoint(
        string UserId,
        string LoginName,
        string DisplayName,
        DateTimeOffset Local,
        int MinutesOfDay,
        bool IsOdd,
        string Reason);

    public record LoginAnalyticsDto(
        string TimeZone,
        int WorkStartMin,
        int WorkEndMin,
        int P50Min,
        int P90Min,
        IReadOnlyList<LoginPoint> Points);

    public interface ILoginAnalyticsService
    {
        Task<LoginAnalyticsDto> GetAsync(
            int days,
            bool markWeekendOdd,
            TimeZoneInfo timeZone,
            TimeSpan workStart,
            TimeSpan workEnd,
            string? userId = null);
    }

    public class LoginAnalyticsService : ILoginAnalyticsService
    {
        private const int MaximumLookbackDays = 365;

        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;

        public LoginAnalyticsService(ApplicationDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<LoginAnalyticsDto> GetAsync(
            int days,
            bool markWeekendOdd,
            TimeZoneInfo timeZone,
            TimeSpan workStart,
            TimeSpan workEnd,
            string? userId = null)
        {
            var safeDays = Math.Clamp(days, 1, MaximumLookbackDays);
            var toUtc = _clock.UtcNow;
            var fromUtc = toUtc.AddDays(-safeDays);

            var query = _db.AuthEvents
                .AsNoTracking()
                .Where(authEvent =>
                    authEvent.Event == AuthenticationEventNames.LoginSucceeded
                    && authEvent.WhenUtc >= fromUtc
                    && authEvent.WhenUtc <= toUtc);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(authEvent => authEvent.UserId == userId);
            }

            var rows = await query
                .OrderBy(authEvent => authEvent.WhenUtc)
                .Select(authEvent => new { authEvent.UserId, authEvent.WhenUtc })
                .ToListAsync();

            var userIds = rows.Select(row => row.UserId).Distinct().ToArray();
            var users = userIds.Length == 0
                ? new Dictionary<string, UserIdentity>(StringComparer.Ordinal)
                : await _db.Users
                    .AsNoTracking()
                    .Where(user => userIds.Contains(user.Id))
                    .Select(user => new UserIdentity(
                        user.Id,
                        user.UserName ?? user.Email ?? "(deleted)",
                        string.IsNullOrWhiteSpace(user.FullName)
                            ? (user.UserName ?? user.Email ?? "(deleted)")
                            : user.FullName))
                    .ToDictionaryAsync(user => user.Id, StringComparer.Ordinal);

            var points = new List<LoginPoint>(rows.Count);
            var minutes = new List<int>(rows.Count);

            foreach (var row in rows)
            {
                var local = TimeZoneInfo.ConvertTime(row.WhenUtc, timeZone);
                var timeOfDay = local.TimeOfDay;
                var minuteOfDay = (int)Math.Round(timeOfDay.TotalMinutes);
                var outsideHours = timeOfDay < workStart || timeOfDay > workEnd;
                var weekend = local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var isOdd = outsideHours || (markWeekendOdd && weekend);
                var reason = isOdd
                    ? (outsideHours ? "Outside working hours" : "Weekend")
                    : string.Empty;

                var identity = users.TryGetValue(row.UserId, out var resolved)
                    ? resolved
                    : new UserIdentity(row.UserId, "(deleted)", "(deleted)");

                points.Add(new LoginPoint(
                    row.UserId,
                    identity.LoginName,
                    identity.DisplayName,
                    local,
                    minuteOfDay,
                    isOdd,
                    reason));

                minutes.Add(minuteOfDay);
            }

            minutes.Sort();

            return new LoginAnalyticsDto(
                timeZone.Id,
                (int)workStart.TotalMinutes,
                (int)workEnd.TotalMinutes,
                Percentile(minutes, 50),
                Percentile(minutes, 90),
                points);
        }

        private static int Percentile(IList<int> sortedMinutes, int percentile)
        {
            if (sortedMinutes.Count == 0)
            {
                return 0;
            }

            if (sortedMinutes.Count == 1)
            {
                return sortedMinutes[0];
            }

            var rank = (percentile / 100.0) * (sortedMinutes.Count - 1);
            var lowerIndex = (int)Math.Floor(rank);
            var upperIndex = (int)Math.Ceiling(rank);
            if (lowerIndex == upperIndex)
            {
                return sortedMinutes[lowerIndex];
            }

            var value = sortedMinutes[lowerIndex]
                + (sortedMinutes[upperIndex] - sortedMinutes[lowerIndex]) * (rank - lowerIndex);
            return (int)Math.Round(value);
        }

        private sealed record UserIdentity(string Id, string LoginName, string DisplayName);
    }
}
