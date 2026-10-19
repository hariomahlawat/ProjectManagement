using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Helpers;

namespace ProjectManagement.Infrastructure;

// Settings for the "login" rate-limiting policy, bound from Security:LoginRateLimit.
public sealed class LoginRateLimitOptions
{
    public const string SectionName = "Security:LoginRateLimit";

    // Sign-in attempts (POSTs) allowed per client address in each window. Sites where
    // many users reach PRISM through one terminal server or proxy should raise this.
    // Out-of-range values are clamped rather than rejected so a typo cannot block sign-in.
    public int PermitLimit { get; set; } = 100;

    public int WindowMinutes { get; set; } = 5;

    public int EffectivePermitLimit => Math.Clamp(PermitLimit, 1, 100_000);

    public TimeSpan EffectiveWindow => TimeSpan.FromMinutes(Math.Clamp(WindowMinutes, 1, 1440));
}

// Partitions sign-in throttling per client address. Opening the login page is never
// throttled, so one busy workstation cannot lock the rest of the office out; account
// guessing is still bounded by Identity lockout (MaxFailedAccessAttempts).
public static class LoginRateLimiting
{
    public const string PolicyName = "login";
    public const string LoginPath = "/Identity/Account/Login";
    public const string ThrottledQueryKey = "throttled";

    public static RateLimitPartition<string> GetPartition(HttpContext context, LoginRateLimitOptions options)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("login:read");
        }

        return RateLimitPartition.GetFixedWindowLimiter("login:" + ClientLabel(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.EffectivePermitLimit,
            Window = options.EffectiveWindow,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    public static string ClientLabel(HttpContext context)
    {
        var client = ClientIp.Get(context);
        return string.IsNullOrEmpty(client) ? "unknown" : client;
    }

    public static bool IsLoginRequest(HttpContext context)
        => context.Request.Path.StartsWithSegments(LoginPath, StringComparison.OrdinalIgnoreCase);

    // Sends a throttled sign-in back to the (unthrottled) login page, which explains the wait.
    public static string BuildThrottledRedirect(HttpContext context)
    {
        var query = new Dictionary<string, string?> { [ThrottledQueryKey] = "true" };
        var returnUrl = context.Request.Query["returnUrl"].ToString();
        if (!string.IsNullOrEmpty(returnUrl))
        {
            query["returnUrl"] = returnUrl;
        }

        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(context.Request.PathBase + LoginPath, query);
    }
}
