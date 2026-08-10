using System;
using ProjectManagement.Services.Stages;
using ProjectManagement.Utilities;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class StageDurationCalculatorTests
{
    [Fact]
    public void InclusiveCalendarDays_SameDay_IsOneDay()
    {
        var date = new DateOnly(2026, 8, 10);
        Assert.Equal(1, StageDurationCalculator.InclusiveCalendarDays(date, date));
    }

    [Fact]
    public void InclusiveCalendarDays_CountsAllElapsedCalendarDates()
    {
        var start = new DateOnly(2026, 8, 7); // Friday
        var end = new DateOnly(2026, 8, 10);  // Monday
        Assert.Equal(4, StageDurationCalculator.InclusiveCalendarDays(start, end));
    }

    [Fact]
    public void CountWorkingDaysInclusive_UsesWeekendAndHolidayPolicy()
    {
        var holiday = new DateOnly(2026, 8, 11);
        var calendar = new WorkingCalendar(new[] { holiday }, includeWeekends: false, skipHolidays: true);

        var start = new DateOnly(2026, 8, 7);  // Friday
        var end = new DateOnly(2026, 8, 13);   // Thursday

        // Fri 07, Mon 10, Wed 12, Thu 13. Weekend and Tue 11 holiday excluded.
        Assert.Equal(4, calendar.CountWorkingDaysInclusive(start, end));
    }
}
