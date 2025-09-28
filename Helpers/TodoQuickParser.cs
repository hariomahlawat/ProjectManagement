using System;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Helpers
{
    /// <summary>
    /// Parses quick-add tokens in a task title such as due date keywords and priority markers.
    /// Supports tokens: tomorrow, today, mon, next mon, !high, !low.
    /// </summary>
    public static class TodoQuickParser
    {
        private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

        public static void Parse(string input, out string title, out DateTimeOffset? dueLocal, out TodoPriority priority)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                title = string.Empty;
                dueLocal = null;
                priority = TodoPriority.Normal;
                return;
            }

            var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            priority = TodoPriority.Normal;
            dueLocal = null;
            var list = new System.Collections.Generic.List<string>();
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);

            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                var wl = w.ToLowerInvariant();
                if (wl == "!high")
                {
                    priority = TodoPriority.High;
                    continue;
                }
                if (wl == "!low")
                {
                    priority = TodoPriority.Low;
                    continue;
                }
                if (wl == "today")
                {
                    dueLocal = new DateTimeOffset(now.Year, now.Month, now.Day, 10, 0, 0, now.Offset);
                    continue;
                }
                if (wl == "tomorrow")
                {
                    var t = now.Date.AddDays(1);
                    dueLocal = new DateTimeOffset(t.Year, t.Month, t.Day, 10, 0, 0, now.Offset);
                    continue;
                }
                if (wl == "mon" || (wl == "next" && i + 1 < words.Length && words[i + 1].ToLowerInvariant() == "mon"))
                {
                    var nextMonday = NextMonday();
                    if (wl == "next")
                    {
                        i++; // consume 'mon'
                        nextMonday = nextMonday.AddDays(7);
                    }
                    dueLocal = new DateTimeOffset(nextMonday.Year, nextMonday.Month, nextMonday.Day, 10, 0, 0, Ist.GetUtcOffset(nextMonday));
                    continue;
                }
                list.Add(w);
            }

            title = string.Join(' ', list).Trim();
        }

        private static DateTime NextMonday()
        {
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist).Date;
            int daysToMon = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
            if (daysToMon == 0)
            {
                daysToMon = 7;
            }
            var mon = now.AddDays(daysToMon);
            return mon;
        }
    }
}
