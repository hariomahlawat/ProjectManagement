using System.Security.Claims;
using ProjectManagement.Services.Usage;

namespace ProjectManagement.Infrastructure.Usage;

/// <summary>
/// Records authenticated navigation to recognised ERP modules after a successful HTML
/// response. Failure is deliberately isolated from the business request.
/// </summary>
public sealed class ErpUsageMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErpUsageMiddleware> _logger;

    public ErpUsageMiddleware(RequestDelegate next, ILogger<ErpUsageMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(
        HttpContext context,
        IErpUsageModuleCatalog modules,
        IUserActivityRecorder recorder)
    {
        var moduleKey = ResolveCandidateModule(context, modules);
        await _next(context);

        if (moduleKey is null || !ShouldRecordCompletedResponse(context))
        {
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        try
        {
            await recorder.RecordAsync(
                userId,
                moduleKey,
                UserActivitySignal.Navigation,
                cancellationToken: context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected after the business response; no usage write is required.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "ERP usage navigation recording failed without affecting the request. Module={ModuleKey}, TraceId={TraceId}",
                moduleKey,
                context.TraceIdentifier);
        }
    }

    private static string? ResolveCandidateModule(
        HttpContext context,
        IErpUsageModuleCatalog modules)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method))
        {
            return null;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/calendar/events", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/notifications", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/Identity", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return modules.ResolvePath(context.Request.Path);
    }

    private static bool ShouldRecordCompletedResponse(HttpContext context)
    {
        if (context.Response.StatusCode is < 200 or >= 300)
        {
            return false;
        }

        var contentType = context.Response.ContentType;
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            return contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
        }

        return context.Request.Headers.Accept.Any(value =>
            value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
    }
}
