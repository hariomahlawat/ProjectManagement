using System;
using System.Globalization;

namespace ProjectManagement.Utilities.PartialDates;

public static class PartialDateDisplay
{
    public static string Format(DateOnly value, PartialDatePrecision precision) => precision switch
    {
        PartialDatePrecision.Year => value.Year.ToString(CultureInfo.InvariantCulture),
        PartialDatePrecision.Month => value.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        PartialDatePrecision.Day => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    public static string? Format(DateOnly? value, PartialDatePrecision precision) =>
        value.HasValue ? Format(value.Value, precision) : null;
}
