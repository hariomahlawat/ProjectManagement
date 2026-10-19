using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Helpers;

namespace ProjectManagement.Infrastructure;

// A fixed-window allowance: PermitLimit per client (login) or per user (uploads), plus a
// ServerPermitLimit shared by everyone so forged client addresses cannot multiply the
// allowance. Out-of-range values are clamped rather than rejected so a configuration typo
// cannot block sign-in, uploads or startup.
public class RateLimitWindowOptions
{
    public int PermitLimit { get; set; }

    public int ServerPermitLimit { get; set; }

    public int WindowMinutes { get; set; }

    public int EffectivePermitLimit => Math.Clamp(PermitLimit, 1, 100_000);

    public int EffectiveServerPermitLimit => Math.Clamp(ServerPermitLimit, EffectivePermitLimit, 1_000_000);

    // Capped at an hour so limiter state for many distinct keys is short-lived.
    public TimeSpan EffectiveWindow => TimeSpan.FromMinutes(Math.Clamp(WindowMinutes, 1, 60));

    public int EffectiveWindowMinutes => (int)EffectiveWindow.TotalMinutes;
}

// Settings for the "login" rate-limiting policy, bound from Security:LoginRateLimit.
// PermitLimit is sign-in attempts (POSTs) per client address in each window; sites where
// many users reach PRISM through one terminal server or proxy should raise it.
// ServerPermitLimit caps attempts from all clients together.
public sealed class LoginRateLimitOptions : RateLimitWindowOptions
{
    public const string SectionName = "Security:LoginRateLimit";

    public LoginRateLimitOptions()
    {
        PermitLimit = 100;
        ServerPermitLimit = 1000;
        WindowMinutes = 5;
    }
}

// Partitions sign-in throttling per client address. Opening the login page is never
// throttled, so one busy workstation cannot lock the rest of the office out. The client
// address comes from X-Forwarded-For when present (see UseForwardedHeaders), which a client
// can forge, so SharedRateLimiters also caps attempts from all clients together;
// account guessing is still bounded by Identity lockout (MaxFailedAccessAttempts).
public static class LoginRateLimiting
{
    public const string PolicyName = "login";
    public const string LoginPath = "/Identity/Account/Login";
    public const string ThrottledQueryKey = "throttled";

    public static RateLimitPartition<string> GetPartition(HttpContext context, LoginRateLimitOptions options, RateLimiter shared)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("login:read");
        }

        return RateLimitPartition.Get("login:" + ClientLabel(context), _ => new GatedRateLimiter(
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.EffectivePermitLimit,
                Window = options.EffectiveWindow,
                QueueLimit = 0,
                AutoReplenishment = true
            }),
            shared));
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
