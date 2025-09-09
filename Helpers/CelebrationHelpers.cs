using System;
using ProjectManagement.Models;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Helpers
{
    public static class CelebrationHelpers
    {
        private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

        public static DateOnly NextOccurrenceLocal(Celebration c, DateOnly today)
        {
            var month = c.Month;
            var day = c.Day;
            var year = today.Year;
            bool isLeapDay = month == 2 && day == 29;
            if (isLeapDay && !DateTime.IsLeapYear(year))
            {
                var candidate = new DateOnly(year, 2, 28);
                if (candidate < today)
                {
                    year++;
                    candidate = DateTime.IsLeapYear(year) ? new DateOnly(year, 2, 29) : new DateOnly(year, 2, 28);
                }
                return candidate;
            }
            var next = new DateOnly(year, month, day);
            if (next < today) next = next.AddYears(1);
            return next;
        }

        public static int DaysAway(DateOnly today, DateOnly next) => (next.ToDateTime(TimeOnly.MinValue) - today.ToDateTime(TimeOnly.MinValue)).Days;

        public static DateTimeOffset ToLocalDateTime(DateOnly date)
        {
            var offset = Ist.GetUtcOffset(new DateTime(date.Year, date.Month, date.Day));
            return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, offset);
        }

        public static string DisplayName(Celebration c) =>
            c.EventType == CelebrationType.Anniversary && !string.IsNullOrWhiteSpace(c.SpouseName)
                ? $"{c.Name} & {c.SpouseName}"
                : c.Name;
    }
}
