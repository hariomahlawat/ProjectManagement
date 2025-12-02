using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Utilities.PartialDates;

// SECTION: Partial date representations and conversions
public enum PartialDatePrecision
{
    None = 0,
    Year = 1,
    Month = 2,
    Day = 3
}

public sealed class PartialDateInput
{
    [Range(1900, 2100)]
    public int? Year { get; set; }

    [Range(1, 12)]
    public int? Month { get; set; }

    [Range(1, 31)]
    public int? Day { get; set; }

    public PartialDatePrecision GetPrecision()
    {
        if (Year.HasValue && !Month.HasValue && !Day.HasValue)
        {
            return PartialDatePrecision.Year;
        }

        if (Year.HasValue && Month.HasValue && !Day.HasValue)
        {
            return PartialDatePrecision.Month;
        }

        if (Year.HasValue && Month.HasValue && Day.HasValue)
        {
            return PartialDatePrecision.Day;
        }

        return PartialDatePrecision.None;
    }
}

public static class PartialDateHelper
{
    // SECTION: Start date conversion
    public static bool TryToStartDate(PartialDateInput input, out DateOnly value, out string? error)
    {
        error = null;
        value = default;

        if (input.Year.HasValue && (input.Year < 1900 || input.Year > 2100))
        {
            error = "Enter a year between 1900 and 2100.";
            return false;
        }

        if (input.Month.HasValue && (input.Month < 1 || input.Month > 12))
        {
            error = "Enter a month between 1 and 12.";
            return false;
        }

        if (input.Day.HasValue && (input.Day < 1 || input.Day > 31))
        {
            error = "Enter a day between 1 and 31.";
            return false;
        }

        if (input.Month.HasValue && !input.Year.HasValue)
        {
            error = "Year is required when specifying a month.";
            return false;
        }

        if (input.Day.HasValue && (!input.Year.HasValue || !input.Month.HasValue))
        {
            error = "Provide year and month when specifying a day.";
            return false;
        }

        switch (input.GetPrecision())
        {
            case PartialDatePrecision.Year:
                value = new DateOnly(input.Year!.Value, 1, 1);
                return true;
            case PartialDatePrecision.Month:
                value = new DateOnly(input.Year!.Value, input.Month!.Value, 1);
                return true;
            case PartialDatePrecision.Day:
                var daysInMonth = DateTime.DaysInMonth(input.Year!.Value, input.Month!.Value);
                if (input.Day > daysInMonth)
                {
                    error = $"Day must be between 1 and {daysInMonth} for the selected month.";
                    return false;
                }

                value = new DateOnly(input.Year!.Value, input.Month!.Value, input.Day!.Value);
                return true;
            case PartialDatePrecision.None:
                error = "At least the year is required.";
                return false;
            default:
                error = "Invalid date input.";
                return false;
        }
    }

    // SECTION: Completion date conversion
    public static bool TryToCompletionDate(PartialDateInput input, out DateOnly value, out string? error)
    {
        error = null;
        value = default;

        if (input.Year.HasValue && (input.Year < 1900 || input.Year > 2100))
        {
            error = "Enter a year between 1900 and 2100.";
            return false;
        }

        if (input.Month.HasValue && (input.Month < 1 || input.Month > 12))
        {
            error = "Enter a month between 1 and 12.";
            return false;
        }

        if (input.Day.HasValue && (input.Day < 1 || input.Day > 31))
        {
            error = "Enter a day between 1 and 31.";
            return false;
        }

        if (input.Month.HasValue && !input.Year.HasValue)
        {
            error = "Year is required when specifying a month.";
            return false;
        }

        if (input.Day.HasValue && (!input.Year.HasValue || !input.Month.HasValue))
        {
            error = "Provide year and month when specifying a day.";
            return false;
        }

        switch (input.GetPrecision())
        {
            case PartialDatePrecision.Year:
            {
                var year = input.Year!.Value;
                value = new DateOnly(year, 12, 31);
                return true;
            }
            case PartialDatePrecision.Month:
            {
                var year = input.Year!.Value;
                var month = input.Month!.Value;
                var lastDay = DateTime.DaysInMonth(year, month);
                value = new DateOnly(year, month, lastDay);
                return true;
            }
            case PartialDatePrecision.Day:
                var lastDayOfMonth = DateTime.DaysInMonth(input.Year!.Value, input.Month!.Value);
                if (input.Day > lastDayOfMonth)
                {
                    error = $"Day must be between 1 and {lastDayOfMonth} for the selected month.";
                    return false;
                }

                value = new DateOnly(input.Year!.Value, input.Month!.Value, input.Day!.Value);
                return true;
            case PartialDatePrecision.None:
                error = "Completion year is required when ToT is completed.";
                return false;
            default:
                error = "Invalid date input.";
                return false;
        }
    }
}
