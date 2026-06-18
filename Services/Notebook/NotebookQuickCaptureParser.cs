using System.Globalization;
using System.Text.RegularExpressions;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Notebook;

public sealed record NotebookQuickCaptureResult(string Title, NotebookItemType Type, NotebookPriority Priority, DateTimeOffset? ReminderAtUtc, IReadOnlyList<string> Tags);

public static partial class NotebookQuickCaptureParser
{
    private static readonly TimeZoneInfo Ist = IstClock.TimeZone;

    // SECTION: Lightweight token parser for offline quick capture
    public static NotebookQuickCaptureResult Parse(string input, NotebookItemType? forcedType = null)
    {
        var text = (input ?? string.Empty).Trim();
        var tags = TagRegex().Matches(text).Select(m => m.Groups[1].Value.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToArray();
        text = TagRegex().Replace(text, " ");
        var priority = text.Contains("!high", StringComparison.OrdinalIgnoreCase) ? NotebookPriority.High : text.Contains("!low", StringComparison.OrdinalIgnoreCase) ? NotebookPriority.Low : NotebookPriority.Normal;
        text = Regex.Replace(text, "!(high|low)", " ", RegexOptions.IgnoreCase);
        var type = forcedType ?? InferType(ref text);
        var reminder = InferReminderUtc(ref text);
        if (reminder.HasValue && forcedType is null && type == NotebookItemType.Note) type = NotebookItemType.Reminder;
        var title = Regex.Replace(text, "\\s+", " ").Trim();
        return new NotebookQuickCaptureResult(string.IsNullOrWhiteSpace(title) ? "Untitled" : title, type, priority, reminder, tags);
    }

    private static NotebookItemType InferType(ref string text)
    {
        foreach (var pair in new[] { ("sticky", NotebookItemType.Sticky), ("checklist", NotebookItemType.Checklist), ("idea", NotebookItemType.Idea), ("draft", NotebookItemType.Draft), ("remind", NotebookItemType.Reminder) })
        {
            if (Regex.IsMatch(text, $"(^|\\s){pair.Item1}(\\s|$)", RegexOptions.IgnoreCase)) { text = Regex.Replace(text, $"(^|\\s){pair.Item1}(\\s|$)", " ", RegexOptions.IgnoreCase); return pair.Item2; }
        }
        return NotebookItemType.Note;
    }

    private static DateTimeOffset? InferReminderUtc(ref string text)
    {
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        DateTime? day = null;
        if (Regex.IsMatch(text, "\\btoday\\b", RegexOptions.IgnoreCase)) { day = localNow.Date; text = Regex.Replace(text, "\\btoday\\b", " ", RegexOptions.IgnoreCase); }
        else if (Regex.IsMatch(text, "\\btomorrow\\b", RegexOptions.IgnoreCase)) { day = localNow.Date.AddDays(1); text = Regex.Replace(text, "\\btomorrow\\b", " ", RegexOptions.IgnoreCase); }
        else
        {
            var m = Regex.Match(text, "\\b(next\\s+)?(mon|tue|wed|thu|fri|sat|sun)\\b", RegexOptions.IgnoreCase);
            if (m.Success) { day = NextWeekday(localNow.Date, m.Groups[2].Value, m.Groups[1].Success); text = text.Remove(m.Index, m.Length); }
        }
        if (day is null) return null;
        var hour = 9; var minute = 0; var time = Regex.Match(text, "\\b(1[0-2]|0?[1-9])(?::([0-5][0-9]))?\\s*(am|pm)\\b", RegexOptions.IgnoreCase);
        if (time.Success) { hour = int.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture); minute = time.Groups[2].Success ? int.Parse(time.Groups[2].Value, CultureInfo.InvariantCulture) : 0; if (time.Groups[3].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) && hour < 12) hour += 12; if (time.Groups[3].Value.Equals("am", StringComparison.OrdinalIgnoreCase) && hour == 12) hour = 0; text = text.Remove(time.Index, time.Length); }
        var local = new DateTimeOffset(day.Value.Year, day.Value.Month, day.Value.Day, hour, minute, 0, Ist.GetUtcOffset(day.Value));
        return TimeZoneInfo.ConvertTime(local, TimeZoneInfo.Utc);
    }

    private static DateTime NextWeekday(DateTime start, string token, bool forceNext) { var target = Array.IndexOf(new[] { "sun", "mon", "tue", "wed", "thu", "fri", "sat" }, token[..3].ToLowerInvariant()); var delta = ((target - (int)start.DayOfWeek) + 7) % 7; if (delta == 0 || forceNext) delta += 7; return start.AddDays(delta); }
    [GeneratedRegex("#([A-Za-z0-9_-]{1,64})")] private static partial Regex TagRegex();
}
