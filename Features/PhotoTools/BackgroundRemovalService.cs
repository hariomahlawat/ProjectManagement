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

    /// <summary>Finds the subject of the photo in <paramref name="photoBytes"/> and returns its mask as a greyscale PNG.</summary>
    Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, CancellationToken cancellationToken);
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
        var concurrent = Math.Clamp(_options.CurrentValue.MaximumConcurrent, 1, 4);
        _turn = new SemaphoreSlim(concurrent, concurrent);
    }

    public BackgroundModelReadiness Readiness() => _readiness.Check();

    public async Task<BackgroundRemovalResult> MaskAsync(byte[] photoBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photoBytes);
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

        if (photoBytes.Length > options.MaximumUploadBytes)
        {
            return new(BackgroundRemovalOutcome.TooLarge);
        }

        // The size is read from the file's header before any pixels are decoded.
        using var data = SKData.CreateCopy(photoBytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return new(BackgroundRemovalOutcome.NotAPhoto);
        }

        if (Math.Max(codec.Info.Width, codec.Info.Height) > options.MaximumSide)
        {
            return new(BackgroundRemovalOutcome.TooLarge);
        }

        if (Interlocked.Increment(ref _waiting) > options.MaximumWaiting + Math.Max(1, options.MaximumConcurrent))
        {
            Interlocked.Decrement(ref _waiting);
            return new(BackgroundRemovalOutcome.Busy);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 600)));
        var entered = false;
        try
        {
            await _turn.WaitAsync(timeout.Token);
            entered = true;
            var stopwatch = Stopwatch.StartNew();
            using var photo = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            var decoded = codec.GetPixels(photo.Info, photo.GetPixels());
            if (decoded is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                return new(BackgroundRemovalOutcome.NotAPhoto);
            }

            // ONNX Runtime blocks while it works; it runs on a pool thread so the request thread is free.
            var mask = await Task.Run(() => _segmenter.Segment(photo, readiness.ModelPath, timeout.Token), timeout.Token);
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

            Interlocked.Decrement(ref _waiting);
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
