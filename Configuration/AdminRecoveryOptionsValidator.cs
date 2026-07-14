using Microsoft.Extensions.Options;

namespace ProjectManagement.Configuration;

public sealed class AdminRecoveryOptionsValidator : IValidateOptions<AdminRecoveryOptions>
{
    public ValidateOptionsResult Validate(string? name, AdminRecoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.DueSoonDays is < 1 or > 90)
            failures.Add("AdminRecovery:DueSoonDays must be between 1 and 90.");
        if (options.DefaultPageSize < 10 || options.DefaultPageSize > options.MaximumPageSize)
            failures.Add("AdminRecovery:DefaultPageSize must be between 10 and MaximumPageSize.");
        if (options.MaximumPageSize is < 10 or > 500)
            failures.Add("AdminRecovery:MaximumPageSize must be between 10 and 500.");
        if (options.MaximumBulkDocuments is < 1 or > 500)
            failures.Add("AdminRecovery:MaximumBulkDocuments must be between 1 and 500.");
        if (options.RecentOperationCount is < 1 or > 50)
            failures.Add("AdminRecovery:RecentOperationCount must be between 1 and 50.");
        if (options.LegacyImportPreviewRows is < 10 or > 1000)
            failures.Add("AdminRecovery:LegacyImportPreviewRows must be between 10 and 1000.");
        if (options.LegacyImportStagingMinutes is < 5 or > 240)
            failures.Add("AdminRecovery:LegacyImportStagingMinutes must be between 5 and 240.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
