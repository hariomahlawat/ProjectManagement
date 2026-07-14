using ProjectManagement.Configuration;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminRecoveryOptionsValidatorTests
{
    private readonly AdminRecoveryOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = _validator.Validate(null, new AdminRecoveryOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_BulkLimitAboveMaximum_Fails()
    {
        var options = new AdminRecoveryOptions { MaximumBulkDocuments = 501 };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(nameof(AdminRecoveryOptions.MaximumBulkDocuments), StringComparison.Ordinal));
    }
}
