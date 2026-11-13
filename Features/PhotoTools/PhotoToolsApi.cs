using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Features.PhotoTools;

/// <summary>
/// The two server endpoints of Photo Tools, both for signed-in users only: whether Remove
/// background is set up, and the mask of one photo. The photo is read into memory, used and
/// dropped; the answer carries only the mask and is never cached.
/// </summary>
public static class PhotoToolsApi
{
    public const string StatusPath = "/api/photo-tools/background/status";
    public const string MaskPath = "/api/photo-tools/background/mask";

    private static readonly HashSet<string> PhotoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    public const string NotSetUpMessage =
        "Remove background is not set up on this PRISM server yet. Ask your PRISM administrator.";

    public static IServiceCollection AddPhotoTools(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<BackgroundRemovalOptions>(configuration.GetSection(BackgroundRemovalOptions.SectionName));
        services.AddSingleton<IBackgroundModelReadinessService, BackgroundModelReadinessService>();
        services.AddSingleton<IBackgroundSegmenter, OnnxBackgroundSegmenter>();
        services.AddSingleton<IBackgroundRemovalService, BackgroundRemovalService>();
        return services;
    }

    public static IEndpointRouteBuilder MapPhotoToolsApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(StatusPath, Status).RequireAuthorization();
        endpoints.MapPost(MaskPath, MaskAsync).RequireAuthorization();
        return endpoints;
    }

    private static IResult Status(
        HttpContext httpContext,
        IBackgroundRemovalService service,
        IOptionsMonitor<BackgroundRemovalOptions> options)
    {
        NoStore(httpContext);
        var readiness = service.Readiness();
        var settings = options.CurrentValue;
        return Results.Ok(new
        {
            available = readiness.IsReady,
            state = StateName(readiness.State),
            message = readiness.IsReady ? null : NotSetUpMessage,
            attribution = readiness.IsReady ? settings.Model.Attribution : null,
            maximumSide = settings.MaximumSide,
            maximumBytes = settings.MaximumUploadBytes
        });
    }

    private static async Task<IResult> MaskAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IBackgroundRemovalService service,
        IOptionsMonitor<BackgroundRemovalOptions> options,
        CancellationToken cancellationToken)
    {
        NoStore(httpContext);
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Problem(StatusCodes.Status400BadRequest, "The request could not be validated. Reload the page and try again.");
        }

        var contentType = httpContext.Request.ContentType?.Split(';')[0].Trim();
        if (contentType is null || !PhotoTypes.Contains(contentType))
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "Send the photo as PNG, JPG or WEBP.");
        }

        var limit = options.CurrentValue.MaximumUploadBytes;
        if (httpContext.Request.ContentLength > limit)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "This photo is too large to send.");
        }

        var sizeLimit = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = limit;
        }

        byte[] photo;
        try
        {
            photo = await ReadBodyAsync(httpContext.Request.Body, limit, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "This photo is too large to send.");
        }
        catch (BadHttpRequestException)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "This photo is too large to send.");
        }

        var result = await service.MaskAsync(photo, cancellationToken);
        switch (result.Outcome)
        {
            case BackgroundRemovalOutcome.Done:
                httpContext.Response.Headers["X-Mask-Width"] = result.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                httpContext.Response.Headers["X-Mask-Height"] = result.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.File(result.MaskPng!, "image/png");
            case BackgroundRemovalOutcome.Unavailable:
                return Problem(StatusCodes.Status503ServiceUnavailable, NotSetUpMessage);
            case BackgroundRemovalOutcome.Busy:
                httpContext.Response.Headers.RetryAfter = "5";
                return Problem(StatusCodes.Status503ServiceUnavailable, "PRISM is removing other backgrounds right now. Try again in a few seconds.");
            case BackgroundRemovalOutcome.NotAPhoto:
                return Problem(StatusCodes.Status415UnsupportedMediaType, "This file could not be read as a photo.");
            case BackgroundRemovalOutcome.TooLarge:
                return Problem(StatusCodes.Status413PayloadTooLarge, "This photo is too large to send.");
            case BackgroundRemovalOutcome.TimedOut:
                return Problem(StatusCodes.Status503ServiceUnavailable, "This took too long and was stopped. Try again, or try a smaller photo.");
            default:
                return Problem(StatusCodes.Status500InternalServerError, "The background could not be removed from this photo.");
        }
    }

    private static async Task<byte[]> ReadBodyAsync(Stream body, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                throw new InvalidDataException("The request body is larger than allowed.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static IResult Problem(int status, string message) => Results.Json(new { message }, statusCode: status);

    private static void NoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
    }

    private static string StateName(BackgroundModelState state) => state switch
    {
        BackgroundModelState.Ready => "ready",
        BackgroundModelState.Disabled => "disabled",
        BackgroundModelState.NotConfigured => "not-configured",
        BackgroundModelState.FileMissing => "model-missing",
        BackgroundModelState.ChecksumNotConfigured => "checksum-not-configured",
        BackgroundModelState.ChecksumMismatch => "checksum-mismatch",
        _ => "unknown"
    };
}
