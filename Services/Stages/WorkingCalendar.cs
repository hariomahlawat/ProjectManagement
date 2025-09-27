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
}
