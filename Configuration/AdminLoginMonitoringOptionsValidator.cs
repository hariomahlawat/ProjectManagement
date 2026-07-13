using Microsoft.Extensions.Options;

namespace ProjectManagement.Configuration;

public sealed class AdminLoginMonitoringOptionsValidator : IValidateOptions<AdminLoginMonitoringOptions>
{
    public ValidateOptionsResult Validate(string? name, AdminLoginMonitoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.WorkdayStart < TimeSpan.Zero || options.WorkdayStart >= TimeSpan.FromDays(1))
        {
            failures.Add("AdminLoginMonitoring:WorkdayStart must be between 00:00 and 23:59:59.");
        }

        if (options.WorkdayEnd <= TimeSpan.Zero || options.WorkdayEnd > TimeSpan.FromDays(1))
        {
            failures.Add("AdminLoginMonitoring:WorkdayEnd must be greater than 00:00 and no later than 24:00.");
        }

        if (options.WorkdayEnd <= options.WorkdayStart)
        {
            failures.Add("AdminLoginMonitoring:WorkdayEnd must be later than WorkdayStart.");
        }

        if (options.MaximumLookbackDays is < 1 or > 3660)
        {
            failures.Add("AdminLoginMonitoring:MaximumLookbackDays must be between 1 and 3660.");
        }

        if (options.DefaultLookbackDays is < 1
            || options.DefaultLookbackDays > options.MaximumLookbackDays)
        {
            failures.Add("AdminLoginMonitoring:DefaultLookbackDays must be between 1 and MaximumLookbackDays.");
        }

        if (options.MaximumChartPoints is < 250 or > 100_000)
        {
            failures.Add("AdminLoginMonitoring:MaximumChartPoints must be between 250 and 100000.");
        }

        if (options.DefaultReviewPageSize is < 10
            || options.DefaultReviewPageSize > options.MaximumReviewPageSize)
        {
            failures.Add("AdminLoginMonitoring:DefaultReviewPageSize must be between 10 and MaximumReviewPageSize.");
        }

        if (options.MaximumReviewPageSize is < 10 or > 500)
        {
            failures.Add("AdminLoginMonitoring:MaximumReviewPageSize must be between 10 and 500.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
