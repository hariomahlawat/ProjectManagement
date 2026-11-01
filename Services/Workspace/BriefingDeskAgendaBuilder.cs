using System.Globalization;
using ProjectManagement.Infrastructure;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

// SECTION: Briefing desk side column — the PO's own reminders and calendar
// "Next 14 days" used to mix reminders overdue for days and pinned notes with no date among the
// coming events. The panel now groups them: Overdue (reminders whose date has passed), Coming up
// (reminders and events from today for 14 days, in date order, at most ComingLimit), and Pinned
// (pinned notes with no date, or one beyond the window). Each group is capped, with a count of
// the rest. Pure, so the grouping is unit-tested.
public static class BriefingDeskAgendaBuilder
{
    public const int WindowDays = 14;
    public const int ComingLimit = 5;
    public const int OverdueLimit = 3;
    public const int PinnedLimit = 3;

    public static BriefingDeskAgendaVm Build(
        IReadOnlyList<WorkspaceReminderVm> reminders,
        IReadOnlyList<WorkspaceUpcomingEventVm> events,
        int eventTotalCount,
        DateOnly istToday)
    {
        var startTodayUtc = IstClock.StartOfDayIstToUtc(istToday);
        var windowEndUtc = IstClock.StartOfDayIstToUtc(istToday.AddDays(WindowDays));

        var overdueAll = reminders
            .Where(reminder => reminder.DueAtUtc is { } due && due < startTodayUtc)
            .OrderBy(reminder => reminder.DueAtUtc)
            .ToList();
        var overdue = overdueAll
            .Take(OverdueLimit)
            .Select(reminder =>
            {
                var days = istToday.DayNumber - DateOnly.FromDateTime(IstClock.ToIst(reminder.DueAtUtc!.Value).DateTime).DayNumber;
                return new BriefingDeskAgendaEntryVm(
                    reminder.Title,
                    $"Overdue by {days} day{(days == 1 ? string.Empty : "s")}",
                    reminder.OpenUrl,
                    "bi-bell");
            })
            .ToList();

        var comingReminders = reminders
            .Where(reminder => reminder.DueAtUtc is { } due && due >= startTodayUtc && due < windowEndUtc)
            .Select(reminder => (At: reminder.DueAtUtc!.Value, Entry: new BriefingDeskAgendaEntryVm(
                reminder.Title,
                $"{DayLabel(DateOnly.FromDateTime(IstClock.ToIst(reminder.DueAtUtc!.Value).DateTime), istToday)} · {IstClock.ToIst(reminder.DueAtUtc!.Value).ToString("HH:mm", CultureInfo.InvariantCulture)}",
                reminder.OpenUrl,
                reminder.IsPinned ? "bi-pin-angle" : "bi-bell")))
            .ToList();
        var comingEvents = events
            .Select(item => (At: item.StartUtc, Entry: new BriefingDeskAgendaEntryVm(
                item.Title,
                $"{DayLabel(item.LocalDate, istToday)} · {item.TimeLabel}",
                item.OpenUrl,
                "bi-calendar-event")))
            .ToList();
        var coming = comingReminders
            .Concat(comingEvents)
            .OrderBy(pair => pair.At)
            .Take(ComingLimit)
            .Select(pair => pair.Entry)
            .ToList();
        // The event query returns only its first few, so the total comes from its own count.
        var comingTotal = comingReminders.Count + Math.Max(eventTotalCount, comingEvents.Count);

        // A pinned note with a date inside the window is listed by its date above.
        var pinnedOther = reminders
            .Where(reminder => reminder.IsPinned && (reminder.DueAtUtc is null || reminder.DueAtUtc >= windowEndUtc))
            .ToList();
        var pinned = pinnedOther
            .Take(PinnedLimit)
            .Select(reminder => new BriefingDeskAgendaEntryVm(
                reminder.Title,
                reminder.DueAtUtc is { } due ? $"Pinned · {IstClock.ToIst(due).ToString("d MMM yyyy", CultureInfo.InvariantCulture)}" : "Pinned",
                reminder.OpenUrl,
                "bi-pin-angle"))
            .ToList();

        return new BriefingDeskAgendaVm
        {
            Overdue = overdue,
            OverdueMoreCount = overdueAll.Count - overdue.Count,
            Coming = coming,
            ComingMoreCount = Math.Max(0, comingTotal - coming.Count),
            Pinned = pinned,
            PinnedMoreCount = Math.Max(0, pinnedOther.Count - pinned.Count)
        };
    }

    private static string DayLabel(DateOnly date, DateOnly today) =>
        date == today ? "Today"
        : date == today.AddDays(1) ? "Tomorrow"
        : date.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
