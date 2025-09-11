using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using ProjectManagement.Models;

namespace ProjectManagement.Helpers;

public static class RecurrenceExpander
{
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    public sealed record Occ(DateTimeOffset Start, DateTimeOffset End, string InstanceId);

    public static IEnumerable<Occ> Expand(Event ev, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        if (string.IsNullOrWhiteSpace(ev.RecurrenceRule))
            return new[] { new Occ(ev.StartUtc, ev.EndUtc, ev.Id.ToString("N")) };

        var startLocal = TimeZoneInfo.ConvertTime(ev.StartUtc, Tz);
        var endLocal = TimeZoneInfo.ConvertTime(ev.EndUtc, Tz);

        var ical = new CalendarEvent
        {
            DtStart = new CalDateTime(startLocal.DateTime, Tz.Id),
            DtEnd = new CalDateTime(endLocal.DateTime, Tz.Id)
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
            var sUtc = TimeZoneInfo.ConvertTimeToUtc(o.Period.StartTime.AsSystemLocal, Tz);
            var eUtc = TimeZoneInfo.ConvertTimeToUtc(o.Period.EndTime.AsSystemLocal, Tz);
            var id = $"{ev.Id:N}_{sUtc:yyyyMMddTHHmmssZ}";
            return new Occ(new DateTimeOffset(sUtc), new DateTimeOffset(eUtc), id);
        });
    }
}
