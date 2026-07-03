using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Loads the compact, read-only calendar preview used by the Project Officer workspace.
/// The query follows the same recurrence and celebration rules as the main Calendar page,
/// while keeping the workspace payload bounded to the next fourteen days.
/// </summary>
internal static class WorkspaceUpcomingEventQuery
{
    private const int WindowDays = 14;
    private const int MaximumItems = 8;

    public static async Task<IReadOnlyList<WorkspaceUpcomingEventVm>> LoadAsync(
        ApplicationDbContext db,
        string userId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var normalizedUtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var nowUtc = new DateTimeOffset(normalizedUtcNow);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, IstClock.TimeZone).Date);
        var windowStart = CelebrationHelpers.ToLocalDateTime(localToday).ToUniversalTime();
        var windowEnd = windowStart.AddDays(WindowDays);

        var events = await db.Events
            .AsNoTracking()
            .Where(calendarEvent =>
                !calendarEvent.IsDeleted &&
                ((calendarEvent.RecurrenceRule != null &&
                  calendarEvent.StartUtc < windowEnd &&
                  (calendarEvent.RecurrenceUntilUtc == null || calendarEvent.RecurrenceUntilUtc >= windowStart)) ||
                 (calendarEvent.StartUtc < windowEnd && calendarEvent.EndUtc > windowStart)))
            .ToListAsync(cancellationToken);

        var items = new List<WorkspaceUpcomingEventVm>();
        foreach (var calendarEvent in events)
        {
            IEnumerable<RecurrenceExpander.Occ> occurrences;
            try
            {
                occurrences = RecurrenceExpander
                    .Expand(calendarEvent, windowStart, windowEnd)
                    .ToList();
            }
            catch
            {
                // A malformed recurrence rule must not make the workspace unavailable.
                occurrences = Array.Empty<RecurrenceExpander.Occ>();
            }

            foreach (var occurrence in occurrences)
            {
                items.Add(BuildEvent(calendarEvent, occurrence, localToday));
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

            items.AddRange(BuildCelebrations(celebrations, windowStart, windowEnd));
        }

        return items
            .Where(item =>
                item.StartUtc < windowEnd &&
                item.EndUtc > (item.IsAllDay ? windowStart : nowUtc))
            .OrderBy(item => item.StartUtc)
            .ThenBy(item => item.IsAllDay ? 0 : 1)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumItems)
            .ToList();
    }

    private static WorkspaceUpcomingEventVm BuildEvent(
        Event calendarEvent,
        RecurrenceExpander.Occ occurrence,
        DateOnly today)
    {
        var localStart = TimeZoneInfo.ConvertTime(occurrence.Start, IstClock.TimeZone);
        var localEnd = TimeZoneInfo.ConvertTime(occurrence.End, IstClock.TimeZone);
        var localDate = DateOnly.FromDateTime(localStart.Date);
        var category = GetEventCategory(calendarEvent.Category);

        return new WorkspaceUpcomingEventVm
        {
            InstanceId = occurrence.InstanceId,
            SeriesId = calendarEvent.Id,
            Title = calendarEvent.Title,
            CategoryLabel = category.Label,
            GroupLabel = GetGroupLabel(localDate, today),
            DateLabel = localDate.ToString("dd MMM yyyy"),
            TimeLabel = FormatTime(calendarEvent.IsAllDay, localStart, localEnd),
            Location = calendarEvent.Location,
            Icon = category.Icon,
            Tone = category.Tone,
            LocalDate = localDate,
            StartUtc = occurrence.Start,
            EndUtc = occurrence.End,
            IsAllDay = calendarEvent.IsAllDay,
            IsCelebration = false,
            OpenUrl = "/Calendar"
        };
    }

    private static IEnumerable<WorkspaceUpcomingEventVm> BuildCelebrations(
        IEnumerable<Celebration> celebrations,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var localWindowStart = TimeZoneInfo.ConvertTime(windowStart, IstClock.TimeZone);
        var today = DateOnly.FromDateTime(localWindowStart.Date);

        foreach (var celebration in celebrations)
        {
            var occurrenceDate = CelebrationHelpers.NextOccurrenceLocal(celebration, today);
            if (occurrenceDate >= today.AddDays(WindowDays))
            {
                continue;
            }

            var start = CelebrationHelpers.ToLocalDateTime(occurrenceDate);
            var end = CelebrationHelpers.ToLocalDateTime(occurrenceDate.AddDays(1));
            if (start >= windowEnd || end <= windowStart)
            {
                continue;
            }

            var isBirthday = celebration.EventType == CelebrationType.Birthday;
            var categoryLabel = isBirthday ? "Birthday" : "Anniversary";
            var displayName = CelebrationHelpers.DisplayName(celebration);

            yield return new WorkspaceUpcomingEventVm
            {
                InstanceId = $"celebration-{celebration.Id:N}-{occurrenceDate:yyyyMMdd}",
                SeriesId = celebration.Id,
                Title = $"{categoryLabel}: {displayName}",
                CategoryLabel = categoryLabel,
                GroupLabel = GetGroupLabel(occurrenceDate, today),
                DateLabel = occurrenceDate.ToString("dd MMM yyyy"),
                TimeLabel = "All day",
                Icon = isBirthday ? "bi-cake2" : "bi-heart",
                Tone = "celebration",
                LocalDate = occurrenceDate,
                StartUtc = start.ToUniversalTime(),
                EndUtc = end.ToUniversalTime(),
                IsAllDay = true,
                IsCelebration = true,
                OpenUrl = "/Calendar"
            };
        }
    }

    private static (string Label, string Icon, string Tone) GetEventCategory(EventCategory category)
        => category switch
        {
            EventCategory.Visit => ("Visit", "bi-person-badge", "visit"),
            EventCategory.Insp => ("Inspection", "bi-clipboard2-check", "inspection"),
            EventCategory.Conference => ("Conference", "bi-people", "conference"),
            _ => ("Event", "bi-calendar-event", "event")
        };

    private static string GetGroupLabel(DateOnly date, DateOnly today)
    {
        var daysAway = date.DayNumber - today.DayNumber;
        return daysAway switch
        {
            <= 0 => "Today",
            1 => "Tomorrow",
            <= 7 => "Next 7 days",
            _ => "Later"
        };
    }

    private static string FormatTime(bool isAllDay, DateTimeOffset localStart, DateTimeOffset localEnd)
    {
        if (isAllDay)
        {
            return "All day";
        }

        return localStart.Date == localEnd.Date
            ? $"{localStart:HH:mm}–{localEnd:HH:mm}"
            : $"{localStart:dd MMM HH:mm}–{localEnd:dd MMM HH:mm}";
    }
}
