using System;
using System.Globalization;

namespace ProjectManagement.Utilities.PartialDates;

public static class PartialDateDisplay
{
    /// <summary>
    /// Formats a stored date at its recorded precision. A date stored without a precision (rows
    /// saved before precision was tracked) shows as the full date: a stored date is never blank.
    /// </summary>
    public static string Format(DateOnly value, PartialDatePrecision precision) => precision switch
    {
        PartialDatePrecision.Year => value.Year.ToString(CultureInfo.InvariantCulture),
        PartialDatePrecision.Month => value.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        _ => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
    };

    public static string? Format(DateOnly? value, PartialDatePrecision precision) =>
        value.HasValue ? Format(value.Value, precision) : null;
}
