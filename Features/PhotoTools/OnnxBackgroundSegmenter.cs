using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace ProjectManagement.Features.PhotoTools;

/// <summary>A mask the size of the photo: one byte per pixel, 255 is subject and 0 background.</summary>
public sealed record BackgroundMask(int Width, int Height, byte[] Alpha);

public interface IBackgroundSegmenter
{
    /// <summary>Finds the subject of a photo. The model must be ready (see <see cref="IBackgroundModelReadinessService"/>).</summary>
    BackgroundMask Segment(SKBitmap photo, string modelPath, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the approved segmentation model with ONNX Runtime on the server's processor. The model is
/// loaded on first use and kept; it is loaded again only when its file changes. One inference uses
/// at most <see cref="BackgroundRemovalOptions.IntraOpThreads"/> threads.
/// </summary>
public sealed class OnnxBackgroundSegmenter : IBackgroundSegmenter, IDisposable
{
    private readonly IOptionsMonitor<BackgroundRemovalOptions> _options;
    private readonly ILogger<OnnxBackgroundSegmenter> _logger;
    private readonly object _gate = new();
    private InferenceSession? _session;
    private string? _sessionPath;
    private bool _disposed;

    public OnnxBackgroundSegmenter(
        IOptionsMonitor<BackgroundRemovalOptions> options,
        ILogger<OnnxBackgroundSegmenter> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public BackgroundMask Segment(SKBitmap photo, string modelPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var model = _options.CurrentValue.Model;
        var session = SessionFor(modelPath);

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

        var output = results.First().AsTensor<float>();
        var values = output.ToArray();
        if (values.Length != model.InputWidth * model.InputHeight)
        {
            throw new InvalidDataException(
                $"The model returned {values.Length} values; a {model.InputWidth} x {model.InputHeight} mask was expected.");
        }

        var small = BackgroundMaskMath.ToMask(values, model.OutputActivation, model.OutputNormalization);
        var alpha = BackgroundMaskMath.Resize(small, model.InputWidth, model.InputHeight, photo.Width, photo.Height);
        return new BackgroundMask(photo.Width, photo.Height, alpha);
    }

    private InferenceSession SessionFor(string path)
    {
        lock (_gate)
        {
            if (_session is not null && _sessionPath == path)
            {
                return _session;
            }

            _session?.Dispose();
            var options = _options.CurrentValue;
            using var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = Math.Clamp(options.IntraOpThreads, 1, Math.Max(1, Environment.ProcessorCount))
            };
            _logger.LogInformation(
                "Loading the background removal model {ModelKey} {ModelVersion} from {ModelPath}.",
                options.Model.Key,
                options.Model.Version,
                path);
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

            _session = session;
            _sessionPath = path;
            return _session;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _session?.Dispose();
            _session = null;
        }
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

        var mean = Channels(model.Mean, 0f);
        var std = Channels(model.Std, 1f);
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

    private static float[] Channels(float[]? values, float fallback)
    {
        if (values is { Length: 3 } && values.All(value => float.IsFinite(value) && (fallback == 0f || value > 0f)))
        {
            return values;
        }

        return new[] { fallback, fallback, fallback };
    }
}
