using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace ProjectManagement.Features.PhotoTools;

public enum BackgroundRemovalOutcome
{
    Done,
    Unavailable,
    Busy,
    NotAPhoto,
    TooLarge,
    TimedOut,
    Failed
}

public sealed record BackgroundRemovalResult(
    BackgroundRemovalOutcome Outcome,
    byte[]? MaskPng = null,
    int Width = 0,
    int Height = 0,
    BackgroundModelState? ModelState = null);

public interface IBackgroundRemovalService
{
    BackgroundModelReadiness Readiness();

    /// <summary>
    /// Finds the subject of the photo in <paramref name="photoBytes"/> and returns its mask as a
    /// greyscale PNG, taking (and giving back) a place in the queue itself.
    /// </summary>
    Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, CancellationToken cancellationToken);

    /// <summary>The same with a <paramref name="place"/> already taken by <see cref="TryTakePlace"/>.</summary>
    Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, IDisposable place, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a place in the queue before the photo is read, or null when the queue is full. A
    /// request holds its place while its photo arrives, so slow uploads count against the queue
    /// and never hold more photos in memory than it allows. Disposing the place gives it back.
    /// </summary>
    IDisposable? TryTakePlace();
}

/// <summary>
/// Remove background on the server. The photo is held in memory for the request only: it is never
/// written to disk, stored, or logged. Photos take turns, so the model never runs on more than
/// <see cref="BackgroundRemovalOptions.MaximumConcurrent"/> at once; when too many are waiting the
/// answer is "busy" rather than a slower PRISM for everyone.
/// </summary>
public sealed class BackgroundRemovalService : IBackgroundRemovalService
{
    private readonly IOptionsMonitor<BackgroundRemovalOptions> _options;
    private readonly IBackgroundModelReadinessService _readiness;
    private readonly IBackgroundSegmenter _segmenter;
    private readonly ILogger<BackgroundRemovalService> _logger;
    private readonly SemaphoreSlim _turn;
    // How many photos run at once is fixed when PRISM starts (a change needs a restart), and the
    // queue is admitted against that same number, so the two never disagree.
    private readonly int _concurrent;
    private int _waiting;

    public BackgroundRemovalService(
        IOptionsMonitor<BackgroundRemovalOptions> options,
        IBackgroundModelReadinessService readiness,
        IBackgroundSegmenter segmenter,
        ILogger<BackgroundRemovalService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _segmenter = segmenter ?? throw new ArgumentNullException(nameof(segmenter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _concurrent = Math.Clamp(_options.CurrentValue.MaximumConcurrent, 1, 4);
        _turn = new SemaphoreSlim(_concurrent, _concurrent);
    }

    public BackgroundModelReadiness Readiness() => _readiness.Check();

    public IDisposable? TryTakePlace()
    {
        if (Interlocked.Increment(ref _waiting) > _options.CurrentValue.EffectiveWaiting + _concurrent)
        {
            Interlocked.Decrement(ref _waiting);
            return null;
        }

        return new Place(this);
    }

    /// <summary>A place in the queue; given back once, however often it is disposed.</summary>
    private sealed class Place(BackgroundRemovalService owner) : IDisposable
    {
        private int _given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _given, 1) == 0)
            {
                Interlocked.Decrement(ref owner._waiting);
            }
        }
    }

    public async Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, CancellationToken cancellationToken)
    {
        using var place = TryTakePlace();
        if (place is null)
        {
            return new(BackgroundRemovalOutcome.Busy);
        }

        return await MaskAsync(photoBytes, place, cancellationToken);
    }

    public async Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, IDisposable place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photoBytes);
        ArgumentNullException.ThrowIfNull(place);
        var options = _options.CurrentValue;
        var readiness = _readiness.Check();
        if (!readiness.IsReady || readiness.ModelPath is null)
        {
            return new(BackgroundRemovalOutcome.Unavailable, ModelState: readiness.State);
        }

        if (photoBytes.Length == 0)
        {
            return new(BackgroundRemovalOutcome.NotAPhoto);
        }

        if (photoBytes.Length > options.EffectiveUploadBytes)
        {
            return new(BackgroundRemovalOutcome.TooLarge);
        }

        // The size is read from the file's header before any pixels are decoded.
        using var data = SKData.CreateCopy(photoBytes);
        using var codec = SKCodec.Create(data);
        // Only the three types the page sends are decoded, whatever the request said it was.
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            return new(BackgroundRemovalOutcome.NotAPhoto);
        }

        if (Math.Max(codec.Info.Width, codec.Info.Height) > options.EffectiveSide)
        {
            return new(BackgroundRemovalOutcome.TooLarge);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 600)));
        var entered = false;
        try
        {
            await _turn.WaitAsync(timeout.Token);
            entered = true;
            // The approval is checked again now: the model may have been replaced while this
            // photo waited its turn.
            readiness = _readiness.Check();
            if (!readiness.IsReady || readiness.ModelPath is null)
            {
                return new(BackgroundRemovalOutcome.Unavailable, ModelState: readiness.State);
            }

            var stopwatch = Stopwatch.StartNew();
            using var photo = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            var decoded = codec.GetPixels(photo.Info, photo.GetPixels());
            if (decoded is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                return new(BackgroundRemovalOutcome.NotAPhoto);
            }

            // ONNX Runtime blocks while it works; it runs on a pool thread so the request thread is free.
            var approved = readiness;
            var mask = await Task.Run(() => _segmenter.Segment(photo, approved, timeout.Token), timeout.Token);
            var png = EncodeMask(mask);
            _logger.LogInformation(
                "Background removed for a {Width} x {Height} photo in {ElapsedMilliseconds} ms.",
                mask.Width,
                mask.Height,
                stopwatch.ElapsedMilliseconds);
            return new(BackgroundRemovalOutcome.Done, png, mask.Width, mask.Height);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(BackgroundRemovalOutcome.TimedOut);
        }
        catch (OnnxRuntimeException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new(BackgroundRemovalOutcome.TimedOut);
        }
        catch (BackgroundModelChangedException)
        {
            // Swapped between the check and the load: not used until it is checked again.
            return new(BackgroundRemovalOutcome.Unavailable, ModelState: _readiness.Check().State);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The photo itself is never logged; only that this one could not be done.
            _logger.LogError(error, "Background removal failed.");
            return new(BackgroundRemovalOutcome.Failed);
        }
        finally
        {
            if (entered)
            {
                _turn.Release();
            }

            // The place is given back as soon as the photo is done, not when the request ends.
            place.Dispose();
        }
    }

    private static byte[] EncodeMask(BackgroundMask mask)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(mask.Width, mask.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        var rows = bitmap.GetPixels();
        for (var y = 0; y < mask.Height; y += 1)
        {
            System.Runtime.InteropServices.Marshal.Copy(mask.Alpha, y * mask.Width, rows + y * bitmap.RowBytes, mask.Width);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
