using ProjectManagement.Configuration;

namespace ProjectManagement.Tests.Usage;

public sealed class ErpUsageOptionsValidatorTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var result = new ErpUsageOptionsValidator().Validate(null, new ErpUsageOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void DuplicateWorkingDays_AreRejected()
    {
        var options = new ErpUsageOptions
        {
            WorkingDays = [DayOfWeek.Monday, DayOfWeek.Monday]
        };

        var result = new ErpUsageOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SundayAsWorkingDay_IsRejected()
    {
        var options = new ErpUsageOptions
        {
            WorkingDays = [DayOfWeek.Monday, DayOfWeek.Sunday]
        };

        var result = new ErpUsageOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("Sunday", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RetentionShorterThanLookback_IsRejected()
    {
        var options = new ErpUsageOptions
        {
            MaximumLookbackDays = 180,
            RetentionDays = 90
        };

        var result = new ErpUsageOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("RetentionDays", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HeartbeatMustFitInsideIdleWindow()
    {
        var options = new ErpUsageOptions
        {
            HeartbeatIntervalSeconds = 600,
            InteractiveIdleMinutes = 10
        };

        var result = new ErpUsageOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("idle window", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TrackingInceptionWithNonUtcOffset_IsRejected()
    {
        var options = new ErpUsageOptions
        {
            TrackingInceptionUtc = new DateTimeOffset(2026, 7, 14, 13, 0, 0, TimeSpan.FromHours(5.5))
        };

        var result = new ErpUsageOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("UTC", StringComparison.OrdinalIgnoreCase));
    }

}
