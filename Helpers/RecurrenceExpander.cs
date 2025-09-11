using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using ProjectManagement.Models;
using ProjectManagement.Utilities;

namespace ProjectManagement.Helpers;

public static class RecurrenceExpander
{
    private static readonly TimeZoneInfo Tz = TimeZoneHelper.GetIst();

    public sealed record Occ(DateTimeOffset Start, DateTimeOffset End, string InstanceId);

    public static IEnumerable<Occ> Expand(Event ev, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        // No recurrence -> just return the event if it overlaps the window
        if (string.IsNullOrWhiteSpace(ev.RecurrenceRule))
        {
            if (ev.StartUtc < windowEnd && ev.EndUtc > windowStart)
                return new[] { new Occ(ev.StartUtc, ev.EndUtc, ev.Id.ToString("N")) };
            return Array.Empty<Occ>();
        }

        try
        {
            var startLocal = TimeZoneInfo.ConvertTime(ev.StartUtc, Tz);
            var endLocal   = TimeZoneInfo.ConvertTime(ev.EndUtc, Tz);

            var ical = new CalendarEvent
            {
                DtStart = new CalDateTime(startLocal.DateTime, Tz.Id),
                DtEnd   = new CalDateTime(endLocal.DateTime, Tz.Id)
            };

            var r = new RecurrencePattern(ev.RecurrenceRule);
            if (ev.RecurrenceUntilUtc.HasValue)
            {
                var untilLocal = TimeZoneInfo.ConvertTime(ev.RecurrenceUntilUtc.Value, Tz);
                r.Until = untilLocal.DateTime;
            }
            ical.RecurrenceRules.Add(r);

            var occs = ical.GetOccurrences(windowStart.UtcDateTime, windowEnd.UtcDateTime);
            return occs.Select(o =>
            {
                var sUtc = o.Period.StartTime.AsUtc;
                var eUtc = o.Period.EndTime.AsUtc;
                var id = $"{ev.Id:N}_{sUtc:yyyyMMddTHHmmssZ}";
                return new Occ(new DateTimeOffset(sUtc), new DateTimeOffset(eUtc), id);
            });
        }
        catch
        {
            // Bad RRULE or TZ edge case: fail soft as a single instance
            if (ev.StartUtc < windowEnd && ev.EndUtc > windowStart)
                return new[] { new Occ(ev.StartUtc, ev.EndUtc, ev.Id.ToString("N")) };
            return Array.Empty<Occ>();
        }
    }
}
