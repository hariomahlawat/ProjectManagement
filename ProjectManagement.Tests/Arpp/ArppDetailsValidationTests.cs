using System.ComponentModel.DataAnnotations;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;
using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppDetailsValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("unlock")]
    [InlineData("add new")]
    public void UnlockInput_RejectsMissingOrShortReason(string reason)
    {
        var input = new DetailsModel.UnlockInputModel { Reason = reason };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            input,
            new ValidationContext(input),
            results,
            validateAllProperties: true);

        Assert.False(valid);
        Assert.Contains(results, result =>
            result.ErrorMessage?.Contains("reason", StringComparison.OrdinalIgnoreCase) == true ||
            result.ErrorMessage?.Contains("10 characters", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void UnlockInput_AcceptsClearReason()
    {
        var input = new DetailsModel.UnlockInputModel
        {
            Reason = "Add newly received project row under Serial No. 48."
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            input,
            new ValidationContext(input),
            results,
            validateAllProperties: true);

        Assert.True(valid);
        Assert.Empty(results);
    }
}
