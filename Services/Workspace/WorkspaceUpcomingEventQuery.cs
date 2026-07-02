using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Builds the compact, read-only Calendar preview used by the Project Officer workspace.
/// It follows the same recurrence and celebration rules as the Calendar module while keeping
/// the workspace query server-side and bounded.
/// </summary>
public static class WorkspaceUpcomingEventQuery
{
    private const int DefaultWindowDays = 14;
    private const int DefaultMaxItems = 6;

    public static async Task<IReadOnlyList<WorkspaceUpcomingEventVm>> LoadAsync(
        ApplicationDbContext db,
        string userId,
        DateTime utcNow,
        CancellationToken cancellationToken,
        int windowDays = DefaultWindowDays,
        int maxItems = DefaultMaxItems)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (string.IsNullOrWhiteSpace(userId) || windowDays <= 0 || maxItems <= 0)
        {
            return Array.Empty<WorkspaceUpcomingEventVm>();
        }

        var normalizedUtcNow = utcNow.Kind switch
        {
            DateTimeKind.Utc => utcNow,
            DateTimeKind.Local => utcNow.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)
        };
        var boundedWindowDays = Math.Min(windowDays, 60);
        var boundedMaxItems = Math.Min(maxItems, 20);
        var windowStart = new DateTimeOffset(normalizedUtcNow);
        var windowEnd = windowStart.AddDays(boundedWindowDays);

        var events = await db.Events
            .AsNoTracking()
            .Where(calendarEvent =>
                !calendarEvent.IsDeleted &&
                (calendarEvent.RecurrenceRule != null ||
                 (calendarEvent.StartUtc < windowEnd && calendarEvent.EndUtc > windowStart)))
            .ToListAsync(cancellationToken);

        var occurrences = new List<WorkspaceUpcomingEventVm>();
        foreach (var calendarEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<RecurrenceExpander.Occ> expanded;
            try
            {
                expanded = RecurrenceExpander.Expand(calendarEvent, windowStart, windowEnd);
            }
            catch
            {
                expanded = Array.Empty<RecurrenceExpander.Occ>();
            }

            foreach (var occurrence in expanded)
            {
                if (occurrence.End <= windowStart || occurrence.Start >= windowEnd)
                {
                    continue;
                }

                occurrences.Add(BuildEventVm(
                    occurrence.InstanceId,
                    calendarEvent,
                    occurrence.Start,
                    occurrence.End,
                    windowStart));
            }
        }

        var showCelebrations = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (bool?)user.ShowCelebrationsInCalendar)
            .SingleOrDefaultAsync(cancellationToken) ?? true;

        if (showCelebrations)
        {
            var celebrations = await db.Celebrations
                .AsNoTracking()
                .Where(celebration => celebration.DeletedUtc == null)
                .ToListAsync(cancellationToken);

            var localToday = DateOnly.FromDateTime(IstClock.ToIst(normalizedUtcNow));
            foreach (var celebration in celebrations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var occurrenceDate = CelebrationHelpers.NextOccurrenceLocal(celebration, localToday);
                var occurrenceStart = CelebrationHelpers.ToLocalDateTime(occurrenceDate).ToUniversalTime();
                var occurrenceEnd = CelebrationHelpers.ToLocalDateTime(occurrenceDate.AddDays(1)).ToUniversalTime();

                if (occurrenceEnd <= windowStart || occurrenceStart >= windowEnd)
                {
                    continue;
                }

                occurrences.Add(BuildCelebrationVm(
                    celebration,
                    occurrenceDate,
                    occurrenceStart,
                    occurrenceEnd,
                    windowStart));
            }
        }

        return occurrences
            .OrderBy(item => item.StartUtc)
            .ThenByDescending(item => item.IsAllDay)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(boundedMaxItems)
            .ToList();
    }

    private static WorkspaceUpcomingEventVm BuildEventVm(
        string instanceId,
        Event calendarEvent,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateTimeOffset windowStart)
    {
        var category = calendarEvent.Category switch
        {
            EventCategory.Visit => "Visit",
            EventCategory.Insp => "Inspection",
            EventCategory.Conference => "Conference",
            _ => "Event"
        };

        var icon = calendarEvent.Category switch
        {
            EventCategory.Visit => "bi-person-badge",
            EventCategory.Insp => "bi-clipboard-check",
            EventCategory.Conference => "bi-people",
            _ => "bi-calendar-event"
        };

        return BuildVm(
            instanceId,
            calendarEvent.Id,
            calendarEvent.Title,
            category,
            calendarEvent.Location,
            icon,
            tone: "event",
            startUtc,
            endUtc,
            calendarEvent.IsAllDay,
            isCelebration: false,
            windowStart);
    }

    private static WorkspaceUpcomingEventVm BuildCelebrationVm(
        Celebration celebration,
        DateOnly occurrenceDate,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        DateTimeOffset windowStart)
    {
        var isBirthday = celebration.EventType == CelebrationType.Birthday;
        var category = isBirthday ? "Birthday" : "Anniversary";

        return BuildVm(
            $"celebration-{celebration.Id:N}-{occurrenceDate:yyyyMMdd}",
            celebration.Id,
            CelebrationHelpers.DisplayName(celebration),
            category,
            location: null,
            icon: isBirthday ? "bi-gift" : "bi-hearts",
            tone: isBirthday ? "birthday" : "anniversary",
            startUtc,
            endUtc,
            isAllDay: true,
            isCelebration: true,
            windowStart);
    }

    private static WorkspaceUpcomingEventVm BuildVm(
        string instanceId,
        Guid seriesId,
        string title,
        string categoryLabel,
        string? location,
        string icon,
        string tone,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        bool isAllDay,
        bool isCelebration,
        DateTimeOffset windowStart)
    {
        var timezone = IstClock.TimeZone;
        var localStart = TimeZoneInfo.ConvertTime(startUtc, timezone);
        var localEnd = TimeZoneInfo.ConvertTime(endUtc, timezone);
        var localNow = TimeZoneInfo.ConvertTime(windowStart, timezone);
        var localDate = DateOnly.FromDateTime(localStart.DateTime);
        var today = DateOnly.FromDateTime(localNow.DateTime);

        return new WorkspaceUpcomingEventVm
        {
            InstanceId = instanceId,
            SeriesId = seriesId,
            Title = title,
            CategoryLabel = categoryLabel,
            GroupLabel = GroupLabel(localDate, today),
            DateLabel = localDate.ToString("ddd, dd MMM"),
            TimeLabel = isAllDay
                ? "All day"
                : FormatTimeRange(localStart, localEnd),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            Icon = icon,
            Tone = tone,
            LocalDate = localDate,
            StartUtc = startUtc,
            EndUtc = endUtc,
            IsAllDay = isAllDay,
            IsCelebration = isCelebration,
            OpenUrl = "/Calendar"
        };
    }

    private static string GroupLabel(DateOnly date, DateOnly today)
    {
        if (date == today)
        {
            return "Today";
        }

        if (date == today.AddDays(1))
        {
            return "Tomorrow";
        }

        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var endOfWeek = today.AddDays(6 - daysSinceMonday);
        return date <= endOfWeek ? "This week" : "Later";
    }

    private static string FormatTimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (start.Date == end.Date)
        {
            return $"{start:HH:mm}–{end:HH:mm}";
        }

        return $"{start:dd MMM HH:mm}–{end:dd MMM HH:mm}";
    }
}
