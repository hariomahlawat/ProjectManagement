using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace ProjectManagement.Infrastructure;

/// <summary>
/// How often a signed-in user's cookie is re-checked against the database.
///
/// Disabling an account, resetting its password, changing its roles or requesting its
/// deletion rotates the user's security stamp. Identity only notices on its next check, which
/// defaults to every 30 minutes, so a disabled or demoted user kept working (with their old
/// roles) for up to half an hour. Each check is one user lookup per signed-in user per
/// interval; a check that passes also re-issues the cookie with the user's current roles.
/// </summary>
public static class SessionRevalidation
{
    public const string ConfigurationKey = "Security:SessionRevalidationMinutes";
    public const int DefaultMinutes = 1;
    public const int MaximumMinutes = 30;

    /// <summary>
    /// Out-of-range values are clamped rather than rejected, so a configuration typo cannot
    /// stop the application from starting.
    /// </summary>
    public static TimeSpan ResolveInterval(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var minutes = configuration.GetValue(ConfigurationKey, DefaultMinutes);
        return TimeSpan.FromMinutes(Math.Clamp(minutes, DefaultMinutes, MaximumMinutes));
    }

    /// <summary>"within a minute" / "within 5 minutes", for messages that tell an administrator
    /// when a change reaches the user's open sessions.</summary>
    public static string DescribeDelay(TimeSpan interval)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(interval.TotalMinutes));
        return minutes == 1 ? "within a minute" : $"within {minutes} minutes";
    }

    public static void Configure(SecurityStampValidatorOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ValidationInterval = ResolveInterval(configuration);
    }
}
