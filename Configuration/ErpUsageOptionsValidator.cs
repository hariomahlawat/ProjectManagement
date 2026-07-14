using Microsoft.Extensions.Options;

namespace ProjectManagement.Configuration;

public sealed class ErpUsageOptionsValidator : IValidateOptions<ErpUsageOptions>
{
    public ValidateOptionsResult Validate(string? name, ErpUsageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.BucketMinutes is < 1 or > 30)
            failures.Add("ErpUsage:BucketMinutes must be between 1 and 30.");
        if (options.HeartbeatIntervalSeconds is < 60 or > 900)
            failures.Add("ErpUsage:HeartbeatIntervalSeconds must be between 60 and 900.");
        if (options.InteractiveIdleMinutes is < 2 or > 60)
            failures.Add("ErpUsage:InteractiveIdleMinutes must be between 2 and 60.");
        if (options.RetentionDays is < 30 or > 1095)
            failures.Add("ErpUsage:RetentionDays must be between 30 and 1095.");
        if (options.RegularUserThresholdPercent is < 1 or > 100)
            failures.Add("ErpUsage:RegularUserThresholdPercent must be between 1 and 100.");
        if (options.MaximumLookbackDays is < 90 or > 365)
            failures.Add("ErpUsage:MaximumLookbackDays must be between 90 and 365 because the command view supports a 90-day period.");
        if (options.RetentionDays < options.MaximumLookbackDays)
            failures.Add("ErpUsage:RetentionDays must be greater than or equal to MaximumLookbackDays.");
        if (options.HeartbeatIntervalSeconds >= options.InteractiveIdleMinutes * 60)
            failures.Add("ErpUsage:HeartbeatIntervalSeconds must be shorter than the interactive idle window.");
        if (options.MaximumExportRows is < 100 or > 20000)
            failures.Add("ErpUsage:MaximumExportRows must be between 100 and 20000.");
        if (options.WorkingDays is null || options.WorkingDays.Length == 0)
            failures.Add("ErpUsage:WorkingDays must contain at least one day.");
        else if (options.WorkingDays.Distinct().Count() != options.WorkingDays.Length)
            failures.Add("ErpUsage:WorkingDays cannot contain duplicate values.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
