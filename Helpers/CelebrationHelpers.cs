using System;
using System.Collections.Generic;
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

        public static string DisplayName(Celebration c) => DisplayName(c, null);

        /// <summary>
        /// The name shown for a celebration. An entry linked to an account shows that account's
        /// rank and name (<paramref name="accountNames"/>, from CelebrationAccountNames.LoadAsync),
        /// so a promotion or a corrected name shows everywhere at once; the name written on the
        /// entry is used only while it is not linked.
        /// </summary>
        public static string DisplayName(Celebration c, IReadOnlyDictionary<string, string>? accountNames)
        {
            var name = c.UserId != null && accountNames != null && accountNames.TryGetValue(c.UserId, out var accountName)
                ? accountName
                : c.Name;
            return c.EventType == CelebrationType.Anniversary && !string.IsNullOrWhiteSpace(c.SpouseName)
                ? $"{name} & {c.SpouseName}"
                : name;
        }
    }
}
