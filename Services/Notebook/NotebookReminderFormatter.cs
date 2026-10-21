using System.Globalization;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Notebook;

public static class NotebookReminderFormatter
{
    // SECTION: IST-safe reminder display and edit helpers
    public static string? FormatReminder(DateTimeOffset? utc)
    {
        if (!utc.HasValue)
        {
            return null;
        }

        return IstClock
            .ToIst(utc.Value)
            .ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
    }

    // SECTION: Relative reminder text for cards and the editor
    // "Today, 17:00", "Tomorrow, 09:00", "Yesterday, 10:00", "Mon, 09:00" within the
    // coming week, "28 Oct, 09:00" this year and "28 Oct 2027, 09:00" otherwise (IST).
    public static string? FormatReminder(DateTimeOffset? utc, DateTimeOffset nowUtc)
    {
        if (!utc.HasValue)
        {
            return null;
        }

        var local = IstClock.ToIst(utc.Value);
        var today = IstClock.ToIst(nowUtc).Date;
        var dayDifference = (local.Date - today).Days;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);

        var day = dayDifference switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            >= 2 and <= 6 => local.ToString("ddd", CultureInfo.InvariantCulture),
            _ when local.Year == today.Year => local.ToString("d MMM", CultureInfo.InvariantCulture),
            _ => local.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
        };

        return $"{day}, {time}";
    }

    public static string? ToLocalInput(DateTimeOffset? utc)
    {
        if (!utc.HasValue)
        {
            return null;
        }

        return IstClock
            .ToIst(utc.Value)
            .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset? FromLocalInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, IstClock.TimeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
