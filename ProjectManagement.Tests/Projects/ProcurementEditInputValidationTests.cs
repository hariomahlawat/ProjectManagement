using System.ComponentModel.DataAnnotations;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProcurementEditInputValidationTests
{
    // The amounts are stored in decimal(18,2) columns. Anything larger used to reach the
    // database and fail the whole save; it must be a validation error instead. Extra decimal
    // places stay accepted (the column rounds them), as before.
    [Theory]
    [InlineData("0", true)]
    [InlineData("1.234", true)]
    [InlineData("48000000", true)]
    [InlineData("9999999999999999.99", true)]
    [InlineData("10000000000000000", false)]
    [InlineData("-1", false)]
    public void Amounts_FollowTheStoredDecimalLimits(string amount, bool expectedValid)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        foreach (var input in new[]
        {
            new ProcurementEditInput { IpaCost = value },
            new ProcurementEditInput { AonCost = value },
            new ProcurementEditInput { BenchmarkCost = value },
            new ProcurementEditInput { L1Cost = value },
            new ProcurementEditInput { PncCost = value },
        })
        {
            var results = new List<ValidationResult>();
            var valid = Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);
            Assert.Equal(expectedValid, valid);
        }
    }

    [Fact]
    public void Maximum_MatchesTheDecimal18_2Column()
    {
        Assert.Equal("9999999999999999.99", ProcurementEditInput.MaxAmountText);
    }
}
