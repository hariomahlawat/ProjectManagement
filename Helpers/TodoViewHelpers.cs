using System;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Helpers
{
    public static class TodoViewHelpers
    {
        private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

        public static string DueBadge(DateTimeOffset? dueUtc, DateTimeOffset? nowUtc = null)
        {
            if (dueUtc is null) return string.Empty;
            var nowLocal = TimeZoneInfo.ConvertTime(nowUtc ?? DateTimeOffset.UtcNow, Ist);
            var endOfTodayLocal = nowLocal.Date.AddDays(1);
            var dueLocal = TimeZoneInfo.ConvertTime(dueUtc.Value, Ist);
            if (dueLocal < nowLocal) return "Overdue";
            if (dueLocal < endOfTodayLocal) return "Today";
            if (dueLocal < endOfTodayLocal.AddDays(1)) return "Tomorrow";
            return dueLocal.ToString("dd-MMM");
        }
    }
}
