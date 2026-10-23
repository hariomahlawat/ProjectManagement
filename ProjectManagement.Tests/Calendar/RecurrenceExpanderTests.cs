using System;
using System.Linq;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using Xunit;

namespace ProjectManagement.Tests.Calendar;

public sealed class RecurrenceExpanderTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    [Fact]
    public void NodaTime_ShippedIsAtLeastTheVersionIcalNetWasBuiltAgainst()
    {
        // Ical.Net 4.3.0 declares NodaTime 3.1.10 but references 3.1.12. If the older assembly ships,
        // expanding any recurrence throws FileNotFoundException at run time and repeating events
        // silently show only their first occurrence.
        var required = typeof(Ical.Net.CalendarComponents.CalendarEvent).Assembly
            .GetReferencedAssemblies()
            .Single(reference => reference.Name == "NodaTime")
            .Version!;
        var shipped = typeof(NodaTime.Instant).Assembly.GetName().Version!;

        Assert.True(shipped >= required, $"NodaTime {shipped} ships, but Ical.Net needs {required}.");
    }

    [Fact]
    public void WeeklyEvent_RepeatsEveryWeekInTheWindow()
    {
        var ev = new Event
        {
            Id = Guid.NewGuid(),
            Title = "Weekly project review",
            StartUtc = new DateTimeOffset(2026, 10, 5, 9, 30, 0, Ist),
            EndUtc = new DateTimeOffset(2026, 10, 5, 10, 30, 0, Ist),
            RecurrenceRule = "FREQ=WEEKLY;INTERVAL=1;BYDAY=MO"
        };
        Exception? failure = null;

        var occurrences = RecurrenceExpander.Expand(
            ev,
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, Ist),
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, Ist),
            ex => failure = ex).ToList();

        Assert.Null(failure);
        Assert.Equal(new[] { 5, 12, 19, 26 }, occurrences.Select(o => o.Start.ToOffset(Ist).Day));
        Assert.All(occurrences, o => Assert.Equal(TimeSpan.FromHours(1), o.End - o.Start));
        Assert.Equal(4, occurrences.Select(o => o.InstanceId).Distinct().Count());
    }

    [Fact]
    public void BadRule_IsReported_AndTheEventShowsOnce()
    {
        var ev = new Event
        {
            Id = Guid.NewGuid(),
            Title = "Broken",
            StartUtc = new DateTimeOffset(2026, 10, 5, 9, 30, 0, Ist),
            EndUtc = new DateTimeOffset(2026, 10, 5, 10, 30, 0, Ist),
            RecurrenceRule = "FREQ=NOT-A-FREQUENCY"
        };
        Exception? failure = null;

        var occurrences = RecurrenceExpander.Expand(
            ev,
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, Ist),
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, Ist),
            ex => failure = ex).ToList();

        Assert.NotNull(failure);
        Assert.Single(occurrences);
    }
}
