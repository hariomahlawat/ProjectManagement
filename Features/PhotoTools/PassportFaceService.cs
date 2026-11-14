using System.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using SkiaSharp;

namespace ProjectManagement.Features.PhotoTools;

public enum PassportFaceOutcome
{
    Found,
    NoFace,
    Unavailable,
    Busy,
    NotAPhoto,
    TooLarge,
    Failed
}

/// <summary>
/// Where the face is in one photo, for Passport photo to place it on the guide: the largest face's
/// box and its five points (in the detector's order: right eye, left eye, nose tip, right and left
/// corner of the mouth, as seen in the photo), in the photo's own pixels.
/// </summary>
public sealed record PassportFaceResult(
    PassportFaceOutcome Outcome,
    int ImageWidth = 0,
    int ImageHeight = 0,
    LocatedFace? Face = null,
    string? Message = null);

public interface IPassportFaceService
{
    /// <summary>Whether the face detector is installed and ready.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Takes one of the places for finding a face before the photo is read, or null when they are
    /// all in use. A request holds it while its photo arrives, so slow uploads never hold more photos
    /// in memory than there are places. Disposing it gives it back.
    /// </summary>
    IDisposable? TryTakePlace();

    /// <summary>
    /// Finds the largest face in <paramref name="photoBytes"/>; the photo is used in memory only.
    /// Without a <paramref name="place"/> the service takes one itself.
    /// </summary>
    Task<PassportFaceResult> LocateAsync(byte[] photoBytes, CancellationToken cancellationToken, IDisposable? place = null);
}

/// <summary>
/// Passport photo's "place the face for me". It runs only the Photos library's face detector
/// (YuNet): never the face recogniser, no record, nothing about People. The photo is held in
/// memory for the request and dropped; only its size and the time taken are logged. Finding a face
/// takes well under a second, so a small number run at once and any more are answered "busy".
/// </summary>
public sealed class PassportFaceService : IPassportFaceService
{
    private readonly IFaceLocationEngine? _engine;
    private readonly IOptionsMonitor<PassportFaceOptions> _options;
    private readonly ILogger<PassportFaceService> _logger;
    private readonly SemaphoreSlim _turn;

    public PassportFaceService(
        IServiceProvider services,
        IOptionsMonitor<PassportFaceOptions> options,
        ILogger<PassportFaceService> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        // The face engine belongs to the Photos library; without it the feature is simply off.
        _engine = services.GetService<IFaceLocationEngine>();
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // The number of places is read once: a running server keeps the count it started with.
        var atOnce = _options.CurrentValue.EffectiveConcurrent;
        _turn = new SemaphoreSlim(atOnce, atOnce);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (_engine is null)
        {
            return false;
        }

        try
        {
            return (await _engine.DetectorReadinessAsync(cancellationToken)).IsReady;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning(error, "The face detector's readiness could not be checked.");
            return false;
        }
    }

    public IDisposable? TryTakePlace() => _turn.Wait(0) ? new Place(_turn) : null;

    /// <summary>A place for finding a face; given back once, however often it is disposed.</summary>
    private sealed class Place(SemaphoreSlim turn) : IDisposable
    {
        private int _given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _given, 1) == 0)
            {
                turn.Release();
            }
        }
    }

    public async Task<PassportFaceResult> LocateAsync(byte[] photoBytes, CancellationToken cancellationToken, IDisposable? place = null)
    {
        ArgumentNullException.ThrowIfNull(photoBytes);
        var own = place is null ? TryTakePlace() : null;
        if (place is null && own is null)
        {
            return new(PassportFaceOutcome.Busy);
        }

        using var _ = own;
        return await LocateInPlaceAsync(photoBytes, cancellationToken);
    }

    private async Task<PassportFaceResult> LocateInPlaceAsync(byte[] photoBytes, CancellationToken cancellationToken)
    {
        if (_engine is null || !await IsAvailableAsync(cancellationToken))
        {
            return new(PassportFaceOutcome.Unavailable);
        }

        var options = _options.CurrentValue;
        if (photoBytes.Length == 0)
        {
            return new(PassportFaceOutcome.NotAPhoto);
        }

        if (photoBytes.Length > options.EffectiveUploadBytes)
        {
            return new(PassportFaceOutcome.TooLarge);
        }

        // The size and type are read from the header before anything is decoded.
        using (var data = SKData.CreateCopy(photoBytes))
        using (var codec = SKCodec.Create(data))
        {
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0
                || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            {
                return new(PassportFaceOutcome.NotAPhoto);
            }

            if (Math.Max(codec.Info.Width, codec.Info.Height) > options.EffectiveSide)
            {
                return new(PassportFaceOutcome.TooLarge);
            }
        }

        // One face may take this long in all; past it the answer is "busy", and the page lets the
        // user place it by hand. A caller who leaves still cancels it at once.
        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(options.EffectiveTimeout);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var located = await _engine.LocateLargestFaceAsync(photoBytes, timeLimit.Token, options.EffectiveDetectorWait);
            _logger.LogInformation(
                "Face located for a {Width} x {Height} photo in {ElapsedMilliseconds} ms ({Found}).",
                located.ImageWidth,
                located.ImageHeight,
                stopwatch.ElapsedMilliseconds,
                located.Face is null ? "no face" : "face found");
            return located.Face is null
                ? new(PassportFaceOutcome.NoFace, located.ImageWidth, located.ImageHeight)
                : new(PassportFaceOutcome.Found, located.ImageWidth, located.ImageHeight, located.Face);
        }
        catch (InvalidDataException)
        {
            return new(PassportFaceOutcome.NotAPhoto);
        }
        catch (TimeoutException)
        {
            _logger.LogInformation("The face detector was busy with the Photos library; answered busy.");
            return new(PassportFaceOutcome.Busy);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Finding the face took longer than {Seconds} s; answered busy.", options.EffectiveTimeout.TotalSeconds);
            return new(PassportFaceOutcome.Busy);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The photo itself is never logged.
            _logger.LogError(error, "Finding the face failed.");
            return new(PassportFaceOutcome.Failed);
        }
    }
}
