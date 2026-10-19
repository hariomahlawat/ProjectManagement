using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Infrastructure;

// Settings for the "docUpload" rate-limiting policy, bound from Security:DocumentUploadRateLimit.
public sealed class DocumentUploadRateLimitOptions : RateLimitWindowOptions
{
    public const string SectionName = "Security:DocumentUploadRateLimit";

    public DocumentUploadRateLimitOptions()
    {
        PermitLimit = 20;
        WindowMinutes = 1;
    }
}

// Throttles document uploads per signed-in user, so one person's bulk upload cannot block
// everyone else's. Opening the upload page is never throttled. Requires UseRateLimiter to
// run after UseAuthentication so the user is known.
public static class DocumentUploadRateLimiting
{
    public const string PolicyName = "docUpload";
    public const string UploadPath = "/DocumentRepository/Documents/Upload";
    public const string ThrottledQueryKey = "throttled";

    public static RateLimitPartition<string> GetPartition(HttpContext context, DocumentUploadRateLimitOptions options)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter("docUpload:read");
        }

        return RateLimitPartition.GetFixedWindowLimiter("docUpload:" + UploaderLabel(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.EffectivePermitLimit,
            Window = options.EffectiveWindow,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    public static string UploaderLabel(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userId)
            ? "client:" + LoginRateLimiting.ClientLabel(context)
            : "user:" + userId;
    }

    public static bool IsUploadRequest(HttpContext context)
        => context.Request.Path.StartsWithSegments(UploadPath, StringComparison.OrdinalIgnoreCase);

    // Sends a throttled upload back to the (unthrottled) upload page, which explains the wait.
    public static string BuildThrottledRedirect(HttpContext context)
        => Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            context.Request.PathBase + UploadPath, ThrottledQueryKey, "true");
}
