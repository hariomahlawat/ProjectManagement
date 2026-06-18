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
