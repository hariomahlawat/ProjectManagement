using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Services.Stages;

public sealed class WorkingCalendar
{
    private readonly HashSet<DateOnly> _holidays;
    private readonly bool _includeWeekends;
    private readonly bool _skipHolidays;

    public WorkingCalendar(IEnumerable<DateOnly> holidays, bool includeWeekends, bool skipHolidays)
    {
        _includeWeekends = includeWeekends;
        _skipHolidays = skipHolidays;
        _holidays = holidays is HashSet<DateOnly> set ? set : holidays.ToHashSet();
    }

    public bool IsWorkingDay(DateOnly date)
    {
        if (!_includeWeekends && (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday))
        {
            return false;
        }

        if (_skipHolidays && _holidays.Contains(date))
        {
            return false;
        }

        return true;
    }

    public DateOnly NextWorkingDay(DateOnly date)
    {
        var cursor = date;
        do
        {
            cursor = cursor.AddDays(1);
        }
        while (!IsWorkingDay(cursor));

        return cursor;
    }

    public DateOnly AddWorkingDays(DateOnly start, int offset)
    {
        var cursor = start;
        var remaining = offset;
        while (remaining > 0)
        {
            cursor = NextWorkingDay(cursor);
            remaining--;
        }

        return cursor;
    }

    /// <summary>
    /// Counts working days inclusively between two dates using the same weekend
    /// and holiday policy used by plan generation. A reversed range returns zero.
    /// </summary>
    public int CountWorkingDaysInclusive(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return 0;
        }

        var count = 0;
        for (var cursor = start; cursor <= end; cursor = cursor.AddDays(1))
        {
            if (IsWorkingDay(cursor))
            {
                count++;
            }
        }

        return count;
    }
}
