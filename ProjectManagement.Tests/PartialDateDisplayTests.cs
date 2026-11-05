using System;
using ProjectManagement.Utilities.PartialDates;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class PartialDateDisplayTests
{
    [Theory]
    [InlineData(PartialDatePrecision.Year, "2026")]
    [InlineData(PartialDatePrecision.Month, "Dec 2026")]
    [InlineData(PartialDatePrecision.Day, "31 Dec 2026")]
    // Rows saved before precision was tracked: the stored date still shows, in full.
    [InlineData(PartialDatePrecision.None, "31 Dec 2026")]
    public void Format_UsesRecordedPrecision(PartialDatePrecision precision, string expected)
    {
        var result = PartialDateDisplay.Format(new DateOnly(2026, 12, 31), precision);

        Assert.Equal(expected, result);
    }
}
