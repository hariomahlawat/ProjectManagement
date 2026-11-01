using ProjectManagement.Infrastructure;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests.Workspace;

public sealed class BriefingDeskAgendaBuilderTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Fact]
    public void Reminders_AreGroupedAsOverdueComingAndPinned()
    {
        var agenda = BriefingDeskAgendaBuilder.Build(
            new[]
            {
                Reminder("Submit AON file", dayOffset: -11),
                Reminder("Call the firm", dayOffset: 2, hour: 15),
                Reminder("Steering agenda", dayOffset: null, pinned: true),
                Reminder("Pinned and due today", dayOffset: 0, hour: 11, pinned: true)
            },
            new[] { Event("Weekly review", dayOffset: 1, hour: 9) },
            eventTotalCount: 1,
            Today);

        var overdue = Assert.Single(agenda.Overdue);
        Assert.Equal("Submit AON file", overdue.Title);
        Assert.Equal("Overdue by 11 days", overdue.Meta);

        // Reminders and events in date order; a pinned note with a date in the window is listed by it.
        Assert.Equal(new[] { "Pinned and due today", "Weekly review", "Call the firm" }, agenda.Coming.Select(entry => entry.Title));
        Assert.Equal("Today · 11:00", agenda.Coming[0].Meta);
        Assert.Equal("Tomorrow · 09:00–10:00", agenda.Coming[1].Meta);
        Assert.Equal("Tue 3 Nov · 15:00", agenda.Coming[2].Meta);

        var pinned = Assert.Single(agenda.Pinned);
        Assert.Equal("Steering agenda", pinned.Title);
        Assert.False(agenda.IsEmpty);
    }

    [Fact]
    public void Groups_AreCapped_WithACountOfTheRest()
    {
        var reminders = Enumerable.Range(1, 6).Select(day => Reminder($"Overdue {day}", dayOffset: -day))
            .Concat(Enumerable.Range(1, 4).Select(day => Reminder($"Coming {day}", dayOffset: day)))
            .Concat(Enumerable.Range(1, 5).Select(index => Reminder($"Pinned {index}", dayOffset: null, pinned: true)))
            .ToList();
        var events = Enumerable.Range(1, 5).Select(day => Event($"Event {day}", dayOffset: day, hour: 18)).ToList();

        // The event query returned five of its eight.
        var agenda = BriefingDeskAgendaBuilder.Build(reminders, events, eventTotalCount: 8, Today);

        Assert.Equal(BriefingDeskAgendaBuilder.OverdueLimit, agenda.Overdue.Count);
        Assert.Equal(3, agenda.OverdueMoreCount);
        // Oldest first: the one overdue longest leads.
        Assert.Equal("Overdue 6", agenda.Overdue[0].Title);
        Assert.Equal(BriefingDeskAgendaBuilder.ComingLimit, agenda.Coming.Count);
        // Shown in date order: three reminders and two events. The rest are counted apart, since
        // reminders are listed in the Notebook and events in the Calendar.
        Assert.Equal(1, agenda.ComingReminderMoreCount);
        Assert.Equal(8 - 2, agenda.ComingEventMoreCount);
        Assert.Equal(BriefingDeskAgendaBuilder.PinnedLimit, agenda.Pinned.Count);
        Assert.Equal(2, agenda.PinnedMoreCount);
    }

    [Fact]
    public void Reminders_BeyondTheWindow_AreLeftOut_UnlessPinned()
    {
        var agenda = BriefingDeskAgendaBuilder.Build(
            new[]
            {
                Reminder("Next month", dayOffset: 30),
                Reminder("Pinned for next month", dayOffset: 30, pinned: true)
            },
            Array.Empty<WorkspaceUpcomingEventVm>(),
            eventTotalCount: 0,
            Today);

        Assert.Empty(agenda.Coming);
        Assert.Equal("Pinned · 1 Dec 2026", Assert.Single(agenda.Pinned).Meta);
    }

    [Fact]
    public void NothingToShow_IsEmpty()
    {
        var agenda = BriefingDeskAgendaBuilder.Build(
            Array.Empty<WorkspaceReminderVm>(),
            Array.Empty<WorkspaceUpcomingEventVm>(),
            eventTotalCount: 0,
            Today);

        Assert.True(agenda.IsEmpty);
    }

    private static WorkspaceReminderVm Reminder(string title, int? dayOffset, int hour = 10, bool pinned = false) => new()
    {
        ReminderId = Guid.NewGuid(),
        Title = title,
        IsPinned = pinned,
        DueAtUtc = dayOffset is int offset ? IstClock.StartOfDayIstToUtc(Today.AddDays(offset)).AddHours(hour) : null,
        OpenUrl = $"/Notebook/{title}"
    };

    private static WorkspaceUpcomingEventVm Event(string title, int dayOffset, int hour) => new()
    {
        Title = title,
        LocalDate = Today.AddDays(dayOffset),
        StartUtc = IstClock.StartOfDayIstToUtc(Today.AddDays(dayOffset)).AddHours(hour),
        TimeLabel = $"{hour:00}:00–{hour + 1:00}:00",
        OpenUrl = "/Calendar"
    };
}
