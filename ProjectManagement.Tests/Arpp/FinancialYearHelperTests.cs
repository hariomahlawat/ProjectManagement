using ProjectManagement.Utilities;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class FinancialYearHelperTests
{
    [Theory]
    [InlineData(2026, "2026-27")]
    [InlineData(2099, "2099-00")]
    public void Format_UsesStartYearAndTwoDigitEndYear(int startYear, string expected)
        => Assert.Equal(expected, FinancialYearHelper.Format(startYear));

    [Theory]
    [InlineData(2026, 4, 1, 2026)]
    [InlineData(2027, 3, 31, 2026)]
    public void GetStartYear_UsesIndianFinancialYear(
        int year,
        int month,
        int day,
        int expected)
        => Assert.Equal(expected, FinancialYearHelper.GetStartYear(new DateOnly(year, month, day)));
}
