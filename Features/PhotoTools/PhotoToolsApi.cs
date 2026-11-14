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
    public const string FacePath = "/api/photo-tools/face";

    public const string FaceNotSetUpMessage =
        "Placing the face is not set up on this PRISM server. Place it by hand.";

    private static readonly HashSet<string> PhotoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    public const string NotSetUpMessage =
        "Remove background is not set up on this PRISM server. Ask your PRISM administrator.";

    /// <summary>What the screen says for each state in which the tool cannot be used.</summary>
    public static string MessageFor(BackgroundModelState state) => state switch
    {
        BackgroundModelState.Checking =>
            "Remove background is getting ready on the PRISM server. Open this screen again in a minute.",
        BackgroundModelState.FileUnreadable =>
            "Remove background cannot read its model file right now. Try again in a minute, or ask your PRISM administrator.",
        BackgroundModelState.ModelInvalid or BackgroundModelState.RuntimeMissing =>
            "Remove background is not working on this PRISM server. Ask your PRISM administrator to check the server log.",
        _ => NotSetUpMessage
    };

    public static IServiceCollection AddPhotoTools(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<BackgroundRemovalOptions>(configuration.GetSection(BackgroundRemovalOptions.SectionName));
        services.Configure<PassportFaceOptions>(configuration.GetSection(PassportFaceOptions.SectionName));
        services.AddSingleton<IBackgroundModelReadinessService, BackgroundModelReadinessService>();
        services.AddSingleton<IBackgroundSegmenter, OnnxBackgroundSegmenter>();
        services.AddSingleton<IBackgroundRemovalService, BackgroundRemovalService>();
        services.AddSingleton<IPassportFaceService, PassportFaceService>();
        return services;
    }

    public static IEndpointRouteBuilder MapPhotoToolsApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(StatusPath, Status).RequireAuthorization();
        endpoints.MapPost(MaskPath, MaskAsync).RequireAuthorization();
        endpoints.MapPost(FacePath, FaceAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> Status(
        HttpContext httpContext,
        IBackgroundRemovalService service,
        IOptionsMonitor<BackgroundRemovalOptions> options,
        CancellationToken cancellationToken)
    {
        NoStore(httpContext);
        var readiness = service.Readiness();
        var settings = options.CurrentValue;
        // Placing the face is a separate, optional part (the Photos library's detector).
        var face = httpContext.RequestServices.GetService<IPassportFaceService>();
        var faceAvailable = face is not null && await face.IsAvailableAsync(cancellationToken);
        return Results.Ok(new
        {
            available = readiness.IsReady,
            state = StateName(readiness.State),
            message = readiness.IsReady ? null : MessageFor(readiness.State),
            attribution = readiness.IsReady ? settings.Model.Attribution : null,
            maximumSide = settings.EffectiveSide,
            maximumBytes = settings.EffectiveUploadBytes,
            faceAvailable
        });
    }

    /// <summary>
    /// Where the face is in one photo, for Passport photo. Signed in, with the antiforgery token,
    /// the same type and size limits as the mask, and nothing stored: the answer is only the face's
    /// box and five points, never cached.
    /// </summary>
    private static async Task<IResult> FaceAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IOptionsMonitor<PassportFaceOptions> options,
        CancellationToken cancellationToken)
    {
        NoStore(httpContext);
        var settings = options.CurrentValue;
        var limit = settings.EffectiveUploadBytes;
        if (await CheckRequestAsync(httpContext, antiforgery, limit) is { } refused)
        {
            return refused;
        }

        var service = httpContext.RequestServices.GetService<IPassportFaceService>();
        if (service is null || !await service.IsAvailableAsync(cancellationToken))
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, FaceNotSetUpMessage);
        }

        // A place is taken before the photo is read, as for the mask: a photo still arriving counts,
        // so uploads never hold more photos in memory than there are places. Given back on every way out.
        using var place = service.TryTakePlace();
        if (place is null)
        {
            return Busy(httpContext, FaceBusyMessage);
        }

        // One time limit for the whole request, upload and finding the face together, while it holds
        // a place.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.EffectiveTimeout);
        var (photo, unread) = await ReadPhotoAsync(httpContext, limit, deadline.Token, cancellationToken);
        if (photo is null)
        {
            return unread!;
        }

        PassportFaceResult result;
        try
        {
            result = await service.LocateAsync(photo, deadline.Token, place);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request's time limit ran out while the face was being found.
            return Busy(httpContext, FaceBusyMessage);
        }

        return result.Outcome switch
        {
            PassportFaceOutcome.Found or PassportFaceOutcome.NoFace => Results.Ok(new
            {
                found = result.Face is not null,
                imageWidth = result.ImageWidth,
                imageHeight = result.ImageHeight,
                face = result.Face is { } face
                    ? new { left = face.Left, top = face.Top, width = face.Width, height = face.Height, confidence = face.Confidence }
                    : null,
                // Right eye, left eye, nose tip, right and left corner of the mouth, as seen in the photo.
                points = result.Face?.Points.Select(point => new[] { point.X, point.Y }).ToArray()
            }),
            PassportFaceOutcome.Unavailable => Problem(StatusCodes.Status503ServiceUnavailable, FaceNotSetUpMessage),
            PassportFaceOutcome.Busy => Busy(httpContext, FaceBusyMessage),
            PassportFaceOutcome.NotAPhoto => Problem(StatusCodes.Status415UnsupportedMediaType, "This file could not be read as a photo."),
            PassportFaceOutcome.TooLarge => Problem(StatusCodes.Status413PayloadTooLarge, "This photo is too large to send."),
            _ => Problem(StatusCodes.Status500InternalServerError, "The face could not be found in this photo.")
        };
    }

    private static async Task<IResult> MaskAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IBackgroundRemovalService service,
        IOptionsMonitor<BackgroundRemovalOptions> options,
        CancellationToken cancellationToken)
    {
        NoStore(httpContext);
        var limit = options.CurrentValue.EffectiveUploadBytes;
        if (await CheckRequestAsync(httpContext, antiforgery, limit) is { } refused)
        {
            return refused;
        }

        // Answered before the photo is read: a server that is not set up, or whose queue is full,
        // never holds the photo in memory first. The place is taken now, so a photo still arriving
        // counts against the queue, and given back on every way out.
        var readiness = service.Readiness();
        if (!readiness.IsReady)
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, MessageFor(readiness.State));
        }

        using var place = service.TryTakePlace();
        if (place is null)
        {
            return Busy(httpContext);
        }

        // The upload has its own deadline while it holds a place: a photo that trickles in cannot keep
        // the place (and others on "busy") for longer than one photo may take.
        using var uploadDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        uploadDeadline.CancelAfter(UploadDeadline(options.CurrentValue));
        var (photo, unread) = await ReadPhotoAsync(httpContext, limit, uploadDeadline.Token, cancellationToken);
        if (photo is null)
        {
            return unread!;
        }

        var result = await service.MaskAsync(photo, place, cancellationToken);
        switch (result.Outcome)
        {
            case BackgroundRemovalOutcome.Done:
                httpContext.Response.Headers["X-Mask-Width"] = result.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                httpContext.Response.Headers["X-Mask-Height"] = result.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.File(result.MaskPng!, "image/png");
            case BackgroundRemovalOutcome.Unavailable:
                return Problem(StatusCodes.Status503ServiceUnavailable, MessageFor(result.ModelState ?? BackgroundModelState.Disabled));
            case BackgroundRemovalOutcome.Busy:
                return Busy(httpContext);
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

    /// <summary>
    /// The checks both photo endpoints make before taking a place: the antiforgery token, a PNG, JPG
    /// or WEBP type, and a declared length within <paramref name="limit"/>. The problem to answer,
    /// or null to go on.
    /// </summary>
    private static async Task<IResult?> CheckRequestAsync(HttpContext httpContext, IAntiforgery antiforgery, long limit)
    {
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

        if (httpContext.Request.ContentLength > limit)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, "This photo is too large to send.");
        }

        return null;
    }

    /// <summary>
    /// Reads the photo, at most <paramref name="limit"/> bytes, by <paramref name="deadline"/>: the
    /// photo, or the problem to answer (408 too slow, 413 too large, 400 cut short). A caller who
    /// left (<paramref name="requestAborted"/>) is not answered.
    /// </summary>
    private static async Task<(byte[]? Photo, IResult? Problem)> ReadPhotoAsync(
        HttpContext httpContext,
        long limit,
        CancellationToken deadline,
        CancellationToken requestAborted)
    {
        var sizeLimit = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = limit;
        }

        try
        {
            return (await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, limit, deadline), null);
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            return (null, Problem(StatusCodes.Status408RequestTimeout, "The photo took too long to arrive. Try again."));
        }
        catch (Exception error) when (error is InvalidDataException or BadHttpRequestException)
        {
            return (null, Problem(StatusCodes.Status413PayloadTooLarge, "This photo is too large to send."));
        }
        catch (EndOfStreamException)
        {
            return (null, Problem(StatusCodes.Status400BadRequest, "The photo did not arrive in full. Try again."));
        }
    }

    /// <summary>How long a photo may take to arrive: the same limit as for working on it.</summary>
    internal static TimeSpan UploadDeadline(BackgroundRemovalOptions options)
        => TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 600));

    private const string FaceBusyMessage = "PRISM is placing other faces right now. Try again in a few seconds.";

    private static IResult Busy(HttpContext httpContext, string message = "PRISM is removing other backgrounds right now. Try again in a few seconds.")
    {
        httpContext.Response.Headers.RetryAfter = "5";
        return Problem(StatusCodes.Status503ServiceUnavailable, message);
    }

    /// <summary>
    /// Reads the photo. With a declared length (as the page always sends) it goes straight into one
    /// array of that size, with no growing buffer and no second copy; without one it is read in
    /// pieces up to the limit.
    /// </summary>
    private static async Task<byte[]> ReadBodyAsync(Stream body, long? declaredLength, long limit, CancellationToken cancellationToken)
    {
        if (declaredLength is > 0 and var length && length <= limit)
        {
            var exact = new byte[length];
            await body.ReadExactlyAsync(exact, cancellationToken);
            // More than was declared is refused rather than silently cut.
            if (await body.ReadAsync(new byte[1], cancellationToken) > 0)
            {
                throw new InvalidDataException("The request body is longer than its declared length.");
            }

            return exact;
        }

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
        BackgroundModelState.Checking => "checking",
        BackgroundModelState.FileUnreadable => "model-unreadable",
        BackgroundModelState.ModelInvalid => "model-invalid",
        BackgroundModelState.RuntimeMissing => "runtime-missing",
        _ => "unknown"
    };
}
