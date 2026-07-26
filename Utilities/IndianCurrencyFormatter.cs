using System.Globalization;

namespace ProjectManagement.Utilities;

public static class IndianCurrencyFormatter
{
    private const decimal Crore = 10_000_000m;
    private const decimal Lakh = 100_000m;
    private static readonly CultureInfo IndianCulture = CultureInfo.GetCultureInfo("en-IN");

    public static string FormatRupees(decimal amount, int decimalPlaces = 2)
    {
        var places = Math.Clamp(decimalPlaces, 0, 2);
        return $"₹{amount.ToString($"N{places}", IndianCulture)}";
    }

    public static string FormatCompact(decimal amount)
    {
        var absolute = Math.Abs(amount);
        if (absolute >= Crore)
        {
            return $"₹{(amount / Crore).ToString("0.##", IndianCulture)} Cr";
        }

        if (absolute >= Lakh)
        {
            return $"₹{(amount / Lakh).ToString("0.##", IndianCulture)} Lakh";
        }

        return FormatRupees(amount, 0);
    }

    public static string FormatWithCompact(decimal amount, int decimalPlaces = 2)
        => $"{FormatRupees(amount, decimalPlaces)} · {FormatCompact(amount)}";
}
