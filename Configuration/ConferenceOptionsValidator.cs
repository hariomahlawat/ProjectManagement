using Microsoft.Extensions.Options;

namespace ProjectManagement.Configuration;

public sealed class ConferenceOptionsValidator : IValidateOptions<ConferenceOptions>
{
    private const int MaximumRetentionDays = 730;

    public ValidateOptionsResult Validate(string? name, ConferenceOptions options)
    {
        if (options.CompletedProjectRetentionDays is < 1 or > MaximumRetentionDays)
        {
            return ValidateOptionsResult.Fail(
                $"Conference:{nameof(ConferenceOptions.CompletedProjectRetentionDays)} must be between 1 and {MaximumRetentionDays} days.");
        }

        return ValidateOptionsResult.Success;
    }
}
