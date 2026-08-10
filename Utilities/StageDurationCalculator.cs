using System;

namespace ProjectManagement.Utilities;

/// <summary>
/// Centralises actual-stage duration semantics. Actual elapsed time is expressed
/// as inclusive calendar days: a stage that starts and completes on the same date
/// has a duration of one calendar day.
/// </summary>
public static class StageDurationCalculator
{
    public static int InclusiveCalendarDays(DateOnly start, DateOnly end)
        => end < start ? 0 : end.DayNumber - start.DayNumber + 1;

    public static int? InclusiveCalendarDays(DateOnly? start, DateOnly? end)
        => start.HasValue && end.HasValue && end.Value >= start.Value
            ? InclusiveCalendarDays(start.Value, end.Value)
            : null;
}
