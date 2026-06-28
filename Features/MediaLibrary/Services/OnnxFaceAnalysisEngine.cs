using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SkiaSharp;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Local ONNX face pipeline. Supported detector and embedder layouts are explicit;
/// arbitrary model files are never interpreted heuristically.
/// </summary>
public sealed class OnnxFaceAnalysisEngine : IFaceAnalysisEngine, IDisposable
{
    private static readonly IReadOnlyList<FacePoint> SFaceDestinationLandmarks = new[]
    {
        new FacePoint(38.2946, 51.6963),
        new FacePoint(73.5318, 51.5014),
        new FacePoint(56.0252, 71.7366),
        new FacePoint(41.5493, 92.3655),
        new FacePoint(70.7299, 92.2041)
    };

    private readonly MediaLibraryOptions _options;
    private readonly IFaceModelReadinessService _readiness;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly SemaphoreSlim _analysisGate;
    private readonly ILogger<OnnxFaceAnalysisEngine> _logger;

    private InferenceSession? _detector;
    private InferenceSession? _embedder;
    private bool _disposed;

    public OnnxFaceAnalysisEngine(
        IOptions<MediaLibraryOptions> options,
        IWebHostEnvironment environment,
        IFaceModelReadinessService readiness,
        ILogger<OnnxFaceAnalysisEngine> logger)
    {
        _ = environment ?? throw new ArgumentNullException(nameof(environment));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _analysisGate = new SemaphoreSlim(
            Math.Clamp(_options.People.MaximumConcurrentAssets, 1, 4),
            Math.Clamp(_options.People.MaximumConcurrentAssets, 1, 4));
    }

    public async Task<IReadOnlyList<DetectedFaceData>> AnalyseAsync(
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OnnxFaceAnalysisEngine));
        }
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            throw new InvalidDataException("The image is empty.");
        }

        var readiness = await _readiness.CheckAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            throw new InvalidOperationException(readiness.Message);
        }

        await EnsureSessionsAsync(readiness, cancellationToken);
        await _analysisGate.WaitAsync(cancellationToken);
        try
        {
            using var encoded = SKData.CreateCopy(imageBytes);
            using var bitmap = SKBitmap.Decode(encoded)
                ?? throw new InvalidDataException("The image could not be decoded for face analysis.");

            var detections = Detect(bitmap);
            var selected = FaceGeometry.NonMaximumSuppression(
                detections,
                detection => detection.Rectangle,
                detection => detection.Confidence,
                _options.People.NonMaximumSuppressionThreshold,
                _options.People.MaximumFacesPerAsset);

            var results = new List<DetectedFaceData>(selected.Count);
            foreach (var detection in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rectangle = FaceGeometry.ClampRectangle(detection.Rectangle, bitmap.Width, bitmap.Height);
                if (rectangle.Width < _options.People.MinimumFacePixels
                    || rectangle.Height < _options.People.MinimumFacePixels)
                {
                    continue;
                }

                var quality = FaceQualityEvaluator.Evaluate(
                    bitmap,
                    rectangle,
                    detection.Landmarks,
                    _options.People);
                float[]? embedding = null;
                if (quality.Status == FaceQualityStatus.EmbeddingEligible)
                {
                    embedding = Embed(bitmap, rectangle, detection.Landmarks);
                }

                var normalizedLandmarks = detection.Landmarks?
                    .SelectMany(point => new[]
                    {
                        point.X / bitmap.Width,
                        point.Y / bitmap.Height
                    })
                    .ToArray();

                results.Add(new DetectedFaceData(
                    rectangle.Left / (double)bitmap.Width,
                    rectangle.Top / (double)bitmap.Height,
                    rectangle.Width / (double)bitmap.Width,
                    rectangle.Height / (double)bitmap.Height,
                    detection.Confidence,
                    quality.Score,
                    quality.Status,
                    embedding,
                    normalizedLandmarks,
                    CreateReviewThumbnail(bitmap, rectangle),
                    quality.Signals.Sharpness,
                    quality.Signals.Exposure,
                    quality.Signals.Pose,
                    quality.Signals));
            }

            return results;
        }
        finally
        {
            _analysisGate.Release();
        }
    }

    private IReadOnlyList<RawFaceDetection> Detect(SKBitmap source)
    {
        var model = _options.People.Detector;
        return model.Adapter.Equals("YuNet", StringComparison.OrdinalIgnoreCase)
            ? DetectYuNet(source, model)
            : DetectDecoded(source, model);
    }

    private IReadOnlyList<RawFaceDetection> DetectYuNet(SKBitmap source, FaceModelOptions model)
    {
        var prepared = PrepareYuNetInput(source);
        using var inputBitmap = prepared.Bitmap;
        var tensor = ToTensor(inputBitmap, model);
        using var results = _detector!.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(ResolveInputName(_detector!, model), tensor)
        });

        var output = results.ToDictionary(result => result.Name, StringComparer.Ordinal);
        var detections = new List<RawFaceDetection>();
        foreach (var stride in new[] { 8, 16, 32 })
        {
            var classification = RequireTensor(output, $"cls_{stride}").ToArray();
            var objectness = RequireTensor(output, $"obj_{stride}").ToArray();
            var boxes = RequireTensor(output, $"bbox_{stride}").ToArray();
            var landmarks = RequireTensor(output, $"kps_{stride}").ToArray();
            var columns = prepared.Width / stride;
            var rows = prepared.Height / stride;
            var expectedCells = rows * columns;
            if (classification.Length < expectedCells
                || objectness.Length < expectedCells
                || boxes.Length < expectedCells * 4
                || landmarks.Length < expectedCells * 10)
            {
                throw new InvalidDataException(
                    $"YuNet output tensors for stride {stride} do not match the configured input dimensions.");
            }

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = row * columns + column;
                    var classificationScore = Math.Clamp(classification[index], 0, 1);
                    var objectnessScore = Math.Clamp(objectness[index], 0, 1);
                    var score = Math.Sqrt(classificationScore * objectnessScore);
                    if (score < _options.People.MinimumDetectionConfidence)
                    {
                        continue;
                    }

                    var centerX = (column + boxes[index * 4]) * stride;
                    var centerY = (row + boxes[index * 4 + 1]) * stride;
                    var width = Math.Exp(Math.Clamp(boxes[index * 4 + 2], -10, 10)) * stride;
                    var height = Math.Exp(Math.Clamp(boxes[index * 4 + 3], -10, 10)) * stride;
                    var rectangle = new FaceRectangle(
                        (centerX - width / 2) / prepared.Scale,
                        (centerY - height / 2) / prepared.Scale,
                        width / prepared.Scale,
                        height / prepared.Scale);

                    var points = new FacePoint[5];
                    for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
                    {
                        points[pointIndex] = new FacePoint(
                            (landmarks[index * 10 + pointIndex * 2] + column) * stride / prepared.Scale,
                            (landmarks[index * 10 + pointIndex * 2 + 1] + row) * stride / prepared.Scale);
                    }

                    detections.Add(new RawFaceDetection(rectangle, score, points));
                }
            }
        }

        return detections
            .OrderByDescending(detection => detection.Confidence)
            .Take(_options.People.DetectorTopK)
            .ToList();
    }

    private IReadOnlyList<RawFaceDetection> DetectDecoded(SKBitmap source, FaceModelOptions model)
    {
        using var resized = source.Resize(
            new SKImageInfo(model.InputWidth, model.InputHeight),
            SKFilterQuality.Medium)
            ?? throw new InvalidDataException("The image could not be resized for face detection.");
        var tensor = ToTensor(resized, model);
        using var results = _detector!.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(ResolveInputName(_detector!, model), tensor)
        });

        var boxes = results.FirstOrDefault(result => result.Name == model.BoxesOutputName)?.AsTensor<float>()
            ?? throw new InvalidDataException($"Detector output '{model.BoxesOutputName}' was not found.");
        var scores = results.FirstOrDefault(result => result.Name == model.ScoresOutputName)?.AsTensor<float>()
            ?? throw new InvalidDataException($"Detector output '{model.ScoresOutputName}' was not found.");
        var landmarks = results.FirstOrDefault(result => result.Name == model.LandmarksOutputName)?.AsTensor<float>();
        var boxValues = boxes.ToArray();
        var scoreValues = scores.ToArray();
        var landmarkValues = landmarks?.ToArray();
        var count = Math.Min(boxValues.Length / 4, scoreValues.Length);
        var detections = new List<RawFaceDetection>(Math.Min(count, _options.People.DetectorTopK));

        for (var index = 0; index < count && detections.Count < _options.People.DetectorTopK; index++)
        {
            var score = scoreValues[index];
            if (score < _options.People.MinimumDetectionConfidence)
            {
                continue;
            }

            var x1 = boxValues[index * 4];
            var y1 = boxValues[index * 4 + 1];
            var x2 = boxValues[index * 4 + 2];
            var y2 = boxValues[index * 4 + 3];
            if (model.BoxesAreNormalized)
            {
                x1 *= source.Width;
                x2 *= source.Width;
                y1 *= source.Height;
                y2 *= source.Height;
            }
            else
            {
                x1 *= source.Width / (float)model.InputWidth;
                x2 *= source.Width / (float)model.InputWidth;
                y1 *= source.Height / (float)model.InputHeight;
                y2 *= source.Height / (float)model.InputHeight;
            }

            IReadOnlyList<FacePoint>? points = null;
            if (landmarkValues is not null && landmarkValues.Length >= (index + 1) * 10)
            {
                var parsed = new FacePoint[5];
                for (var pointIndex = 0; pointIndex < parsed.Length; pointIndex++)
                {
                    var x = landmarkValues[index * 10 + pointIndex * 2];
                    var y = landmarkValues[index * 10 + pointIndex * 2 + 1];
                    parsed[pointIndex] = model.BoxesAreNormalized
                        ? new FacePoint(x * source.Width, y * source.Height)
                        : new FacePoint(
                            x * source.Width / model.InputWidth,
                            y * source.Height / model.InputHeight);
                }

                points = parsed;
            }

            detections.Add(new RawFaceDetection(
                new FaceRectangle(x1, y1, x2 - x1, y2 - y1),
                score,
                points));
        }

        return detections;
    }

    private float[] Embed(
        SKBitmap source,
        SKRectI rectangle,
        IReadOnlyList<FacePoint>? landmarks)
    {
        var model = _options.People.Embedder;
        using var aligned = CreateAlignedFace(source, rectangle, landmarks, model);
        var tensor = ToTensor(aligned, model);
        using var results = _embedder!.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(ResolveInputName(_embedder!, model), tensor)
        });
        var output = string.IsNullOrWhiteSpace(model.EmbeddingOutputName)
            ? results.First().AsTensor<float>()
            : results.FirstOrDefault(result => result.Name == model.EmbeddingOutputName)?.AsTensor<float>()
              ?? throw new InvalidDataException(
                  $"Embedding output '{model.EmbeddingOutputName}' was not found.");
        var vector = output.ToArray();
        if (model.EmbeddingDimension > 0 && vector.Length != model.EmbeddingDimension)
        {
            throw new InvalidDataException(
                $"Embedding dimension {vector.Length} does not match configured dimension {model.EmbeddingDimension}.");
        }

        Normalize(vector);
        return vector;
    }

    private static SKBitmap CreateAlignedFace(
        SKBitmap source,
        SKRectI rectangle,
        IReadOnlyList<FacePoint>? landmarks,
        FaceModelOptions model)
    {
        var targetWidth = model.InputWidth;
        var targetHeight = model.InputHeight;
        var aligned = new SKBitmap(targetWidth, targetHeight);
        using var canvas = new SKCanvas(aligned);
        canvas.Clear(SKColors.Black);

        if (model.Adapter.Equals("SFace", StringComparison.OrdinalIgnoreCase)
            && landmarks is { Count: >= 5 }
            && targetWidth == 112
            && targetHeight == 112
            && FaceGeometry.TryCreateSimilarityTransform(
                landmarks.Take(5).ToArray(),
                SFaceDestinationLandmarks,
                out var transform))
        {
            canvas.Save();
            canvas.SetMatrix(transform.ToSkMatrix());
            canvas.DrawBitmap(source, 0, 0);
            canvas.Restore();
            return aligned;
        }

        var side = Math.Max(rectangle.Width, rectangle.Height) * 1.25;
        var centerX = (rectangle.Left + rectangle.Right) / 2d;
        var centerY = (rectangle.Top + rectangle.Bottom) / 2d;
        var square = FaceGeometry.ClampRectangle(
            new FaceRectangle(centerX - side / 2, centerY - side / 2, side, side),
            source.Width,
            source.Height);
        canvas.DrawBitmap(
            source,
            new SKRect(square.Left, square.Top, square.Right, square.Bottom),
            new SKRect(0, 0, targetWidth, targetHeight));
        return aligned;
    }

    private PreparedInput PrepareYuNetInput(SKBitmap source)
    {
        var maximumDimension = Math.Max(320, _options.People.InferenceMaxDimension);
        var scale = Math.Min(1d, maximumDimension / (double)Math.Max(source.Width, source.Height));
        var scaledWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var inputWidth = RoundUp(scaledWidth, 32);
        var inputHeight = RoundUp(scaledHeight, 32);

        using var resized = source.Resize(
            new SKImageInfo(scaledWidth, scaledHeight),
            SKFilterQuality.Medium)
            ?? throw new InvalidDataException("The image could not be resized for YuNet inference.");
        var padded = new SKBitmap(inputWidth, inputHeight);
        using (var canvas = new SKCanvas(padded))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(resized, 0, 0);
        }

        return new PreparedInput(padded, inputWidth, inputHeight, scale);
    }

    private async Task EnsureSessionsAsync(
        FaceModelReadiness readiness,
        CancellationToken cancellationToken)
    {
        if (_detector is not null && _embedder is not null)
        {
            return;
        }

        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            if (_detector is null)
            {
                _detector = CreateSession(readiness.DetectorPath!);
            }

            if (_embedder is null)
            {
                _embedder = CreateSession(readiness.EmbedderPath!);
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private InferenceSession CreateSession(string path)
    {
        using var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / Math.Max(1, _options.People.MaximumConcurrentAssets))
        };
        _logger.LogInformation("Loading approved face model from {ModelPath}.", path);
        return new InferenceSession(path, sessionOptions);
    }


    private static string ResolveInputName(InferenceSession session, FaceModelOptions model)
    {
        if (!string.IsNullOrWhiteSpace(model.InputName)
            && session.InputMetadata.ContainsKey(model.InputName))
        {
            return model.InputName;
        }

        if (session.InputMetadata.Count == 1)
        {
            return session.InputMetadata.Keys.Single();
        }

        throw new InvalidDataException(
            $"Configured input '{model.InputName}' was not found for model '{model.Key}', and the model exposes multiple inputs.");
    }

    private static DenseTensor<float> ToTensor(SKBitmap bitmap, FaceModelOptions model)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, bitmap.Height, bitmap.Width });
        var bgr = model.ChannelOrder.Equals("BGR", StringComparison.OrdinalIgnoreCase);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var first = bgr ? color.Blue : color.Red;
                var third = bgr ? color.Red : color.Blue;
                tensor[0, 0, y, x] = bgr
                    ? NormalizeInput(first, model.MeanB, model.StdB, model.InputScale)
                    : NormalizeInput(first, model.MeanR, model.StdR, model.InputScale);
                tensor[0, 1, y, x] = NormalizeInput(color.Green, model.MeanG, model.StdG, model.InputScale);
                tensor[0, 2, y, x] = bgr
                    ? NormalizeInput(third, model.MeanR, model.StdR, model.InputScale)
                    : NormalizeInput(third, model.MeanB, model.StdB, model.InputScale);
            }
        }

        return tensor;
    }

    private static float NormalizeInput(byte value, float mean, float standardDeviation, float scale)
        => (value - mean) * scale / standardDeviation;

    private static Tensor<float> RequireTensor(
        IReadOnlyDictionary<string, DisposableNamedOnnxValue> output,
        string name)
        => output.TryGetValue(name, out var value)
            ? value.AsTensor<float>()
            : throw new InvalidDataException($"Required YuNet output '{name}' was not found.");

    private static byte[] CreateReviewThumbnail(SKBitmap source, SKRectI rectangle)
    {
        var margin = (int)(Math.Max(rectangle.Width, rectangle.Height) * 0.25);
        var expanded = FaceGeometry.ClampRectangle(
            new FaceRectangle(
                rectangle.Left - margin,
                rectangle.Top - margin,
                rectangle.Width + margin * 2,
                rectangle.Height + margin * 2),
            source.Width,
            source.Height);
        using var crop = new SKBitmap(expanded.Width, expanded.Height);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.DrawBitmap(
                source,
                new SKRect(expanded.Left, expanded.Top, expanded.Right, expanded.Bottom),
                new SKRect(0, 0, expanded.Width, expanded.Height));
        }

        using var image = SKImage.FromBitmap(crop);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 84)
            ?? throw new InvalidDataException("The face review thumbnail could not be encoded.");
        return encoded.ToArray();
    }

    private static void Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        var norm = Math.Sqrt(sum);
        if (norm <= 1e-12)
        {
            throw new InvalidDataException("The embedding model returned a zero vector.");
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / norm);
        }
    }

    private static int RoundUp(int value, int divisor)
        => ((value + divisor - 1) / divisor) * divisor;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detector?.Dispose();
        _embedder?.Dispose();
        _sessionGate.Dispose();
        _analysisGate.Dispose();
    }

    private sealed record RawFaceDetection(
        FaceRectangle Rectangle,
        double Confidence,
        IReadOnlyList<FacePoint>? Landmarks);

    private sealed record PreparedInput(
        SKBitmap Bitmap,
        int Width,
        int Height,
        double Scale);
}
