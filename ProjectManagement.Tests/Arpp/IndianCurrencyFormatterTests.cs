using ProjectManagement.Utilities;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class IndianCurrencyFormatterTests
{
    [Theory]
    [InlineData("10000000", "₹1,00,00,000.00", "₹1 Cr")]
    [InlineData("2500000", "₹25,00,000.00", "₹25 Lakh")]
    [InlineData("99999", "₹99,999.00", "₹99,999")]
    public void FormatsRupeesAndCompactValues_Deterministically(
        string value,
        string expectedRupees,
        string expectedCompact)
    {
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expectedRupees, IndianCurrencyFormatter.FormatRupees(amount));
        Assert.Equal(expectedCompact, IndianCurrencyFormatter.FormatCompact(amount));
    }
}
