using ProjectManagement.Configuration;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminLoginMonitoringOptionsValidatorTests
{
    private readonly AdminLoginMonitoringOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var result = _validator.Validate(null, new AdminLoginMonitoringOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EndBeforeStart_Fails()
    {
        var options = new AdminLoginMonitoringOptions
        {
            WorkdayStart = TimeSpan.FromHours(18),
            WorkdayEnd = TimeSpan.FromHours(8)
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("later than WorkdayStart", StringComparison.Ordinal));
    }
    [Fact]
    public void Validate_InvalidDuplicateWindow_Fails()
    {
        var options = new AdminLoginMonitoringOptions { DuplicateWindowMinutes = 0 };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(nameof(AdminLoginMonitoringOptions.DuplicateWindowMinutes), StringComparison.Ordinal));
    }

}
