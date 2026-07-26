namespace ProjectManagement.Utilities;

public static class FinancialYearHelper
{
    public const int MinimumSupportedStartYear = 2000;
    public const int MaximumSupportedStartYear = 9998;

    public static string Format(int startYear)
    {
        Validate(startYear);
        return $"{startYear}-{(startYear + 1) % 100:D2}";
    }

    public static string FormatFull(int startYear)
    {
        Validate(startYear);
        return $"{startYear}-{startYear + 1}";
    }

    public static DateOnly GetStartDate(int startYear)
    {
        Validate(startYear);
        return new DateOnly(startYear, 4, 1);
    }

    public static DateOnly GetEndDate(int startYear)
    {
        Validate(startYear);
        return new DateOnly(startYear + 1, 3, 31);
    }

    public static int GetStartYear(DateOnly date)
        => date.Month >= 4 ? date.Year : date.Year - 1;

    public static bool Contains(int startYear, DateOnly date)
        => date >= GetStartDate(startYear) && date <= GetEndDate(startYear);

    public static void Validate(int startYear)
    {
        if (startYear is < MinimumSupportedStartYear or > MaximumSupportedStartYear)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startYear),
                startYear,
                $"Financial-year start must be between {MinimumSupportedStartYear} and {MaximumSupportedStartYear}.");
        }
    }
}
