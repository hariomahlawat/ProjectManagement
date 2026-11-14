using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace ProjectManagement.Features.PhotoTools;

/// <summary>A mask the size of the photo: one byte per pixel, 255 is subject and 0 background.</summary>
public sealed record BackgroundMask(int Width, int Height, byte[] Alpha);

public interface IBackgroundSegmenter
{
    /// <summary>
    /// Finds the subject of a photo with the approved model <paramref name="model"/> (from
    /// <see cref="IBackgroundModelReadinessService"/>). Throws <see cref="BackgroundModelChangedException"/>
    /// when the file is no longer the one that was approved.
    /// </summary>
    BackgroundMask Segment(SKBitmap photo, BackgroundModelReadiness model, CancellationToken cancellationToken);

    /// <summary>
    /// True when this approved file, with the current settings, recently failed to load; it is not
    /// loaded again (1 GB each time) until the file or settings change or a few minutes pass.
    /// </summary>
    bool HasFailedToLoad(BackgroundModelReadiness approved) => false;

    /// <summary>Whether ONNX Runtime's native library can be loaded on this server.</summary>
    bool RuntimeAvailable => true;
}

/// <summary>The approved model could not be loaded (wrong settings, a damaged file); not retried at once.</summary>
public sealed class BackgroundModelLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The model file changed after its checksum was checked; it is not used until checked again.</summary>
public sealed class BackgroundModelChangedException : Exception
{
    public BackgroundModelChangedException()
        : base("The background removal model file changed after it was checked.")
    {
    }
}

/// <summary>
/// Runs the approved segmentation model with ONNX Runtime on the server's processor. The model is
/// loaded on first use and kept while it is used; it is loaded again when its file or its settings
/// change, and released after <see cref="BackgroundRemovalOptions.ModelIdleMinutes"/> without a
/// photo or when the tool is turned off. ONNX Runtime's memory arena is off, so the working memory
/// of a photo (gigabytes for RMBG-2.0) goes back after the photo instead of staying at its peak,
/// and the threads are limited by <see cref="ThreadsFor"/>.
/// </summary>
public sealed class OnnxBackgroundSegmenter : IBackgroundSegmenter, IDisposable
{
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<BackgroundRemovalOptions> _options;
    private readonly ILogger<OnnxBackgroundSegmenter> _logger;
    private readonly object _gate = new();
    private readonly Timer _idleTimer;
    // Read once, as BackgroundRemovalService does for its queue: the photos admitted at once and
    // the threads each one gets are worked out from the same number until PRISM restarts.
    private readonly int _concurrent;
    private LoadedModel? _loaded;
    private bool _disposed;
    // The last failed load. It has its own short lock: _gate is held for the whole of a load (up to
    // a minute for a 1 GB model), and a status or upload request must not wait on that to read it.
    private readonly object _failedGate = new();
    private (string Key, DateTime FailedUtc, string Message)? _failed;
    private static readonly TimeSpan FailedLoadRetry = TimeSpan.FromMinutes(5);
    private static readonly Lazy<bool> Runtime = new(() =>
    {
        try
        {
            _ = OrtEnv.Instance();
            return true;
        }
        catch (Exception error) when (error is DllNotFoundException or TypeInitializationException
            or BadImageFormatException or EntryPointNotFoundException or OnnxRuntimeException)
        {
            return false;
        }
    });

    public bool RuntimeAvailable => Runtime.Value;

    public bool HasFailedToLoad(BackgroundModelReadiness approved)
    {
        ArgumentNullException.ThrowIfNull(approved);
        if (!approved.IsReady)
        {
            return false;
        }

        var key = KeyFor(approved, _options.CurrentValue);
        return RecentFailure(key) is not null;
    }

    /// <summary>The message of a load of <paramref name="key"/> that failed recently, or null.</summary>
    private string? RecentFailure(string key)
    {
        lock (_failedGate)
        {
            return _failed is { } failed && failed.Key == key && DateTime.UtcNow - failed.FailedUtc < FailedLoadRetry
                ? failed.Message
                : null;
        }
    }

    public OnnxBackgroundSegmenter(
        IOptionsMonitor<BackgroundRemovalOptions> options,
        ILogger<OnnxBackgroundSegmenter> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _concurrent = Math.Clamp(_options.CurrentValue.MaximumConcurrent, 1, 4);
        _idleTimer = new Timer(_ => ReleaseIdle(DateTime.UtcNow), null, IdleCheckInterval, IdleCheckInterval);
    }

    /// <summary>
    /// Threads for one photo: the setting, but never more than half the server's processors shared
    /// between the <paramref name="concurrent"/> photos that may run at once, and at least one.
    /// <paramref name="concurrent"/> is the number the queue was started with, not the current
    /// setting, which only takes effect after a restart.
    /// </summary>
    public static int ThreadsFor(int intraOpThreads, int concurrent, int processorCount)
    {
        var budget = Math.Max(1, processorCount / 2);
        var share = Math.Max(1, budget / Math.Clamp(concurrent, 1, 4));
        return Math.Clamp(intraOpThreads, 1, share);
    }

    /// <summary>The photos that may run at once, as read when PRISM started.</summary>
    public int Concurrent => _concurrent;

    /// <summary>True while a model is held in memory (for tests and the status page).</summary>
    public bool IsModelLoaded
    {
        get
        {
            lock (_gate)
            {
                return _loaded is not null;
            }
        }
    }

    /// <summary>
    /// Releases the loaded model when no photo is using it and it has not been used for
    /// <see cref="BackgroundRemovalOptions.ModelIdleMinutes"/>, or when the tool is turned off.
    /// Called every 30 seconds; <paramref name="nowUtc"/> is given so tests can move time on.
    /// </summary>
    public void ReleaseIdle(DateTime nowUtc)
    {
        var options = _options.CurrentValue;
        lock (_gate)
        {
            if (_disposed || _loaded is null || _loaded.Users > 0)
            {
                return;
            }

            var idle = nowUtc - _loaded.LastUsedUtc;
            var timedOut = options.ModelIdleMinutes > 0 && idle >= TimeSpan.FromMinutes(options.ModelIdleMinutes);
            if (options.Enabled && !timedOut)
            {
                return;
            }

            _loaded.Session.Dispose();
            _loaded = null;
            _logger.LogInformation(
                options.Enabled
                    ? "Released the background removal model from memory after {IdleMinutes} minutes without a photo."
                    : "Released the background removal model from memory because Remove background was turned off.",
                (int)idle.TotalMinutes);
        }
    }

    public BackgroundMask Segment(SKBitmap photo, BackgroundModelReadiness approved, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(approved);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!approved.IsReady || approved.ModelPath is null)
        {
            throw new BackgroundModelChangedException();
        }

        var options = _options.CurrentValue;
        var model = options.Model;
        var loaded = Acquire(approved, options);
        try
        {
            var session = loaded.Session;
            var inputName = string.IsNullOrWhiteSpace(model.InputName)
                ? session.InputMetadata.Keys.Single()
                : model.InputName;
            var outputName = string.IsNullOrWhiteSpace(model.OutputName)
                ? session.OutputMetadata.Keys.First()
                : model.OutputName;

            var input = BackgroundMaskMath.ToInputTensor(photo, model);
            using var runOptions = new RunOptions();
            // A photo that takes too long, or whose request is gone, stops the inference itself.
            using var stop = cancellationToken.Register(() => runOptions.Terminate = true);
            using var results = session.Run(
                new[] { NamedOnnxValue.CreateFromTensor(inputName, input) },
                new[] { outputName },
                runOptions);
            cancellationToken.ThrowIfCancellationRequested();

            var values = results.First().AsTensor<float>().ToArray();
            if (values.Length != model.InputWidth * model.InputHeight)
            {
                throw new InvalidDataException(
                    $"The model returned {values.Length} values; a {model.InputWidth} x {model.InputHeight} mask was expected.");
            }

            var small = BackgroundMaskMath.ToMask(values, model.OutputActivation, model.OutputNormalization);
            var alpha = BackgroundMaskMath.Resize(small, model.InputWidth, model.InputHeight, photo.Width, photo.Height);
            return new BackgroundMask(photo.Width, photo.Height, alpha);
        }
        finally
        {
            Release(loaded);
        }
    }

    /// <summary>
    /// The loaded model for this approved file and these settings, loading it when the file or
    /// the settings changed (the model can be replaced without a restart). Only the very file that
    /// was approved is loaded: one that differs in size or time from it has been swapped since its
    /// checksum was checked. A model being replaced is released only once no photo is using it.
    /// </summary>
    private LoadedModel Acquire(BackgroundModelReadiness approved, BackgroundRemovalOptions options)
    {
        var path = approved.ModelPath!;
        var key = KeyFor(approved, options);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_loaded is not null && _loaded.Key == key)
            {
                _loaded.Users += 1;
                return _loaded;
            }

            var file = new FileInfo(path);
            if (!file.Exists || file.Length != approved.Length || file.LastWriteTimeUtc != approved.LastWriteUtc)
            {
                throw new BackgroundModelChangedException();
            }

            if (RecentFailure(key) is { } failure)
            {
                throw new BackgroundModelLoadException(failure);
            }

            InferenceSession session;
            try
            {
                session = Load(path, options);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Logged once here; later photos are refused at once until the file or settings
                // change or a few minutes pass, instead of loading 1 GB again for each one.
                lock (_failedGate)
                {
                    _failed = (key, DateTime.UtcNow, error.Message);
                }

                _logger.LogError(error, "The background removal model could not be loaded. It is not tried again for {Minutes} minutes or until its file or settings change.", (int)FailedLoadRetry.TotalMinutes);
                throw new BackgroundModelLoadException(error.Message, error);
            }

            lock (_failedGate)
            {
                _failed = null;
            }

            var fresh = new LoadedModel(session, key) { Users = 1 };
            if (_loaded is not null)
            {
                _loaded.Retired = true;
                if (_loaded.Users == 0)
                {
                    _loaded.Session.Dispose();
                }
            }

            _loaded = fresh;
            return fresh;
        }
    }

    private void Release(LoadedModel loaded)
    {
        lock (_gate)
        {
            loaded.Users -= 1;
            loaded.LastUsedUtc = DateTime.UtcNow;
            if (loaded.Retired && loaded.Users == 0)
            {
                loaded.Session.Dispose();
            }
        }
    }

    /// <summary>What a loaded model stands for: the approved file (path, size, time, checksum) and the settings it was checked against.</summary>
    private string KeyFor(BackgroundModelReadiness approved, BackgroundRemovalOptions options)
    {
        var model = options.Model;
        return string.Join(
            '|',
            approved.ModelPath,
            approved.Length,
            approved.LastWriteUtc.Ticks,
            model.Sha256?.Trim().ToLowerInvariant(),
            model.InputName,
            model.OutputName,
            model.InputWidth,
            model.InputHeight,
            string.Join(',', model.ChannelMean()),
            string.Join(',', model.ChannelStd()),
            ThreadsFor(options.IntraOpThreads, _concurrent, Environment.ProcessorCount));
    }

    private InferenceSession Load(string path, BackgroundRemovalOptions options)
    {
        var threads = ThreadsFor(options.IntraOpThreads, _concurrent, Environment.ProcessorCount);
        using var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = threads,
            // Without the arena each photo's working memory is freed when the photo is done; with it,
            // ONNX Runtime kept its peak (18.9 GB committed after two photos with RMBG-2.0).
            EnableCpuMemArena = false,
            EnableMemoryPattern = false
        };
        // Threads wait without spinning between steps, so an idle photo thread uses no processor.
        sessionOptions.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        sessionOptions.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
        _logger.LogInformation(
            "Loading the background removal model {ModelKey} {ModelVersion} from {ModelPath} with {Threads} threads.",
            options.Model.Key,
            options.Model.Version,
            path,
            threads);
        var session = new InferenceSession(path, sessionOptions);
        try
        {
            BackgroundModelContract.Check(session, options.Model);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        return session;
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        lock (_gate)
        {
            _disposed = true;
            if (_loaded is not null)
            {
                _loaded.Retired = true;
                if (_loaded.Users == 0)
                {
                    _loaded.Session.Dispose();
                }

                _loaded = null;
            }
        }
    }

    private sealed class LoadedModel(InferenceSession session, string key)
    {
        public InferenceSession Session { get; } = session;
        public string Key { get; } = key;
        public int Users { get; set; }
        public bool Retired { get; set; }
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
    }
}

/// <summary>
/// Checks that a model file fits its settings before it is used, so a wrong setting is reported
/// plainly in the server log instead of giving wrong masks.
/// </summary>
public static class BackgroundModelContract
{
    public static void Check(InferenceSession session, BackgroundModelOptions model)
    {
        CheckChannels(model);

        if (!string.IsNullOrWhiteSpace(model.InputName) && !session.InputMetadata.ContainsKey(model.InputName))
        {
            throw new InvalidDataException(
                $"The background removal model has no input named '{model.InputName}'. Its inputs are: {string.Join(", ", session.InputMetadata.Keys)}.");
        }

        if (string.IsNullOrWhiteSpace(model.InputName) && session.InputMetadata.Count != 1)
        {
            throw new InvalidDataException(
                $"The background removal model has {session.InputMetadata.Count} inputs; set Model:InputName to one of: {string.Join(", ", session.InputMetadata.Keys)}.");
        }

        if (!string.IsNullOrWhiteSpace(model.OutputName) && !session.OutputMetadata.ContainsKey(model.OutputName))
        {
            throw new InvalidDataException(
                $"The background removal model has no output named '{model.OutputName}'. Its outputs are: {string.Join(", ", session.OutputMetadata.Keys)}.");
        }

        var input = string.IsNullOrWhiteSpace(model.InputName)
            ? session.InputMetadata.Values.Single()
            : session.InputMetadata[model.InputName];
        var dimensions = input.Dimensions;
        if (dimensions.Length != 4 || (dimensions[1] > 0 && dimensions[1] != 3))
        {
            throw new InvalidDataException(
                $"The background removal model's input has shape [{string.Join(", ", dimensions)}]; an RGB image [1, 3, height, width] was expected.");
        }

        // A model with a fixed input size must be given exactly that size.
        if ((dimensions[2] > 0 && dimensions[2] != model.InputHeight) || (dimensions[3] > 0 && dimensions[3] != model.InputWidth))
        {
            throw new InvalidDataException(
                $"The background removal model takes {dimensions[3]} x {dimensions[2]} images, but Model:InputWidth and Model:InputHeight are {model.InputWidth} x {model.InputHeight}.");
        }
    }

    /// <summary>Mean and Std must be three values each (R, G, B), and every Std above zero.</summary>
    public static void CheckChannels(BackgroundModelOptions model)
    {
        var mean = model.ChannelMean();
        var std = model.ChannelStd();
        if (mean.Length != 3 || std.Length != 3)
        {
            throw new InvalidDataException(
                $"Model:Mean and Model:Std must have three values each (R, G, B); they have {mean.Length} and {std.Length}.");
        }

        if (mean.Any(value => !float.IsFinite(value)) || std.Any(value => !float.IsFinite(value) || value <= 0f))
        {
            throw new InvalidDataException("Model:Mean must be numbers and Model:Std must be numbers above zero.");
        }
    }
}

/// <summary>The arithmetic around the model: preparing its input and turning its output into a mask.</summary>
public static class BackgroundMaskMath
{
    /// <summary>
    /// The photo as the model's input: stretched to the model's size, laid on white where it is
    /// see-through, then per channel (pixel / 255 - mean) / std, in N, C, H, W order.
    /// </summary>
    public static DenseTensor<float> ToInputTensor(SKBitmap photo, BackgroundModelOptions model)
    {
        var width = model.InputWidth;
        var height = model.InputHeight;
        using var scaled = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            canvas.DrawBitmap(photo, new SKRect(0, 0, width, height), paint);
        }

        var mean = model.ChannelMean();
        var std = model.ChannelStd();
        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
        var pixels = scaled.GetPixelSpan();
        var plane = width * height;
        var buffer = tensor.Buffer.Span;
        for (var i = 0; i < plane; i += 1)
        {
            for (var channel = 0; channel < 3; channel += 1)
            {
                buffer[channel * plane + i] = (pixels[i * 4 + channel] / 255f - mean[channel]) / std[channel];
            }
        }

        return tensor;
    }

    /// <summary>The model's output as a mask of bytes, after the activation and stretch its settings ask for.</summary>
    public static byte[] ToMask(float[] values, string activation, string normalization)
    {
        var result = new float[values.Length];
        // "Auto": a model that already gives 0 to 1 is used as it is; one that gives raw scores
        // (values outside 0 to 1) has the sigmoid applied.
        var sigmoid = string.Equals(activation, "Sigmoid", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(activation, "Auto", StringComparison.OrdinalIgnoreCase)
                && values.Any(value => value < -0.01f || value > 1.01f));
        for (var i = 0; i < values.Length; i += 1)
        {
            var value = values[i];
            result[i] = sigmoid ? 1f / (1f + MathF.Exp(-value)) : value;
        }

        if (string.Equals(normalization, "MinMax", StringComparison.OrdinalIgnoreCase) && result.Length > 0)
        {
            var low = result.Min();
            var high = result.Max();
            var range = high - low;
            for (var i = 0; i < result.Length; i += 1)
            {
                result[i] = range > 1e-6f ? (result[i] - low) / range : 0f;
            }
        }

        var mask = new byte[result.Length];
        for (var i = 0; i < result.Length; i += 1)
        {
            mask[i] = (byte)Math.Clamp((int)MathF.Round(result[i] * 255f, MidpointRounding.AwayFromZero), 0, 255);
        }

        return mask;
    }

    /// <summary>Resizes a mask with bilinear blending, pixel centres aligned.</summary>
    public static byte[] Resize(byte[] mask, int width, int height, int targetWidth, int targetHeight)
    {
        if (width == targetWidth && height == targetHeight)
        {
            return (byte[])mask.Clone();
        }

        var result = new byte[targetWidth * targetHeight];
        var scaleX = width / (double)targetWidth;
        var scaleY = height / (double)targetHeight;
        for (var y = 0; y < targetHeight; y += 1)
        {
            var sy = Math.Clamp((y + 0.5) * scaleY - 0.5, 0, height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(height - 1, y0 + 1);
            var ty = sy - y0;
            for (var x = 0; x < targetWidth; x += 1)
            {
                var sx = Math.Clamp((x + 0.5) * scaleX - 0.5, 0, width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(width - 1, x0 + 1);
                var tx = sx - x0;
                var top = mask[y0 * width + x0] * (1 - tx) + mask[y0 * width + x1] * tx;
                var bottom = mask[y1 * width + x0] * (1 - tx) + mask[y1 * width + x1] * tx;
                result[y * targetWidth + x] = (byte)Math.Clamp((int)Math.Round(top * (1 - ty) + bottom * ty), 0, 255);
            }
        }

        return result;
    }
}
