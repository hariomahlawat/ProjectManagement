using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// CPU-only, explainable and deterministic media classifier. No proprietary model,
/// network service or unapproved model weights are used.
/// </summary>
public sealed class MediaClassifier : IMediaClassifier
{
    public const string ClassifierVersion = "deterministic-media-v3";

    private static readonly string[] ScreenshotTerms =
    {
        "screenshot", "screen shot", "screen-shot", "screen_capture", "screen capture",
        "snipping", "snip", "capture", "clipboard"
    };

    private static readonly string[] DocumentTerms =
    {
        "scan", "scanned", "document", "letter", "memo", "page", "form", "certificate", "receipt"
    };

    private static readonly string[] DiagramTerms =
    {
        "diagram", "chart", "graph", "flow", "workflow", "architecture", "schematic", "slide", "ppt"
    };

    private readonly MediaLibraryOptions _options;

    public MediaClassifier(IOptions<MediaLibraryOptions> options)
        => _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task<MediaClassificationResult> ClassifyAsync(
        string path,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ClassifyCoreAsync(Path.GetFileName(path), stream, metadata, cancellationToken);
    }

    public async Task<MediaClassificationResult> ClassifyAsync(
        MediaContentDescriptor content,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.OpenReadAsync(cancellationToken);
        return await ClassifyCoreAsync(content.FileName, stream, metadata, cancellationToken);
    }

    private async Task<MediaClassificationResult> ClassifyCoreAsync(
        string fileNameWithExtension,
        Stream stream,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Classification.Enabled)
            return Result(MediaClassification.Unknown, 0, "Media classification is disabled.");

        if (metadata.Kind == MediaAssetKind.Video)
            return Result(MediaClassification.Unknown, 0, "Video classification is not enabled in this release.");

        var fileName = Path.GetFileNameWithoutExtension(fileNameWithExtension);
        var extension = Path.GetExtension(fileNameWithExtension);
        var signals = new List<string>();
        var scores = new Dictionary<MediaClassification, double>
        {
            [MediaClassification.Photograph] = metadata.HasCameraMetadata ? 0.78 : 0.25,
            [MediaClassification.Screenshot] = 0,
            [MediaClassification.ScannedDocument] = 0,
            [MediaClassification.Diagram] = 0,
            [MediaClassification.PresentationSlide] = 0,
            [MediaClassification.Graphic] = 0
        };

        ApplyNameAndMetadataSignals(fileName, extension, metadata, scores, signals);

        try
        {
            if (stream.CanSeek) stream.Position = 0;
            using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
            var metrics = Measure(image, _options.Classification.AnalysisMaxDimension);
            ApplyVisualSignals(metrics, scores, signals);
        }
        catch (UnknownImageFormatException)
        {
            signals.Add("The image format could not be decoded for visual analysis.");
        }
        catch (InvalidImageContentException)
        {
            signals.Add("The image content could not be decoded for visual analysis.");
        }

        var ordered = scores.OrderByDescending(pair => pair.Value).ToArray();
        var winner = ordered[0];
        var runnerUp = ordered.Length > 1 ? ordered[1].Value : 0;
        var confidence = Math.Clamp(0.52 + ((winner.Value - runnerUp) * 0.55), 0.50, 0.98);

        var requiredScore = winner.Key switch
        {
            MediaClassification.Screenshot => _options.Classification.ScreenshotThreshold,
            MediaClassification.ScannedDocument => _options.Classification.DocumentThreshold,
            MediaClassification.Diagram or MediaClassification.PresentationSlide => _options.Classification.DiagramThreshold,
            _ => _options.Classification.MinimumConfidence
        };

        if (winner.Value < requiredScore)
        {
            signals.Add($"The leading classification score did not meet its configured {requiredScore:P0} threshold.");
            return new MediaClassificationResult(MediaClassification.Unknown, confidence, signals, ClassifierVersion);
        }

        return new MediaClassificationResult(winner.Key, confidence, signals, ClassifierVersion);
    }

    private void ApplyNameAndMetadataSignals(
        string fileName,
        string extension,
        MediaFileMetadata metadata,
        IDictionary<MediaClassification, double> scores,
        ICollection<string> signals)
    {
        if (_options.Classification.ScreenshotDetectionEnabled
            && ScreenshotTerms.Any(term => fileName.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            scores[MediaClassification.Screenshot] += 0.72;
            signals.Add("Filename indicates a screenshot or screen capture.");
        }

        if (_options.Classification.DocumentDetectionEnabled
            && DocumentTerms.Any(term => fileName.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            scores[MediaClassification.ScannedDocument] += 0.48;
            signals.Add("Filename indicates a scanned or document image.");
        }

        if (_options.Classification.DiagramDetectionEnabled
            && DiagramTerms.Any(term => fileName.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            scores[MediaClassification.Diagram] += 0.50;
            signals.Add("Filename indicates a diagram, chart or presentation.");
        }

        if (metadata.HasCameraMetadata)
        {
            scores[MediaClassification.Photograph] += 0.32;
            scores[MediaClassification.Screenshot] -= 0.35;
            signals.Add("Camera metadata strongly supports a photograph.");
        }
        else
        {
            if (_options.Classification.ScreenshotDetectionEnabled)
                scores[MediaClassification.Screenshot] += 0.10;
            scores[MediaClassification.Graphic] += 0.08;
            signals.Add("No camera make or model metadata is present.");
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            if (_options.Classification.ScreenshotDetectionEnabled)
                scores[MediaClassification.Screenshot] += 0.10;
            scores[MediaClassification.Graphic] += 0.07;
        }

        if (_options.Classification.ScreenshotDetectionEnabled
            && metadata.Width.HasValue
            && metadata.Height.HasValue
            && IsCommonScreenDimension(metadata.Width.Value, metadata.Height.Value))
        {
            scores[MediaClassification.Screenshot] += 0.25;
            signals.Add("Dimensions match a common display or mobile screenshot size.");
        }
    }

    private void ApplyVisualSignals(
        VisualMetrics metrics,
        IDictionary<MediaClassification, double> scores,
        ICollection<string> signals)
    {
        if (metrics.FlatPixelRatio >= 0.58)
        {
            if (_options.Classification.ScreenshotDetectionEnabled)
                scores[MediaClassification.Screenshot] += 0.20;
            scores[MediaClassification.Graphic] += 0.22;
            signals.Add("Large flat-colour regions are present.");
        }

        if (metrics.EdgeDensity >= 0.22)
        {
            if (_options.Classification.DiagramDetectionEnabled)
                scores[MediaClassification.Diagram] += 0.24;
            if (_options.Classification.DocumentDetectionEnabled)
                scores[MediaClassification.ScannedDocument] += 0.18;
            signals.Add("High edge density indicates text, line art or diagram content.");
        }

        if (metrics.LightBackgroundRatio >= 0.72 && metrics.EdgeDensity >= 0.12)
        {
            if (_options.Classification.DocumentDetectionEnabled)
                scores[MediaClassification.ScannedDocument] += 0.35;
            signals.Add("A light page-like background with dense foreground marks is present.");
        }

        if (metrics.ColourDiversity <= 0.18 && metrics.EdgeDensity >= 0.16)
        {
            if (_options.Classification.DiagramDetectionEnabled)
                scores[MediaClassification.Diagram] += 0.24;
            signals.Add("Low colour diversity with strong edges supports a diagram or chart.");
        }

        if (metrics.ColourDiversity >= 0.38 && metrics.FlatPixelRatio < 0.45)
        {
            scores[MediaClassification.Photograph] += 0.30;
            signals.Add("Natural colour diversity and limited flat regions support a photograph.");
        }

        if (metrics.WideAspectRatio is >= 1.72 and <= 1.82 && metrics.LightBackgroundRatio >= 0.45)
        {
            if (_options.Classification.DiagramDetectionEnabled)
                scores[MediaClassification.PresentationSlide] += 0.34;
            signals.Add("A presentation-like 16:9 aspect ratio and structured background are present.");
        }
    }

    private static VisualMetrics Measure(Image<Rgba32> source, int maxDimension)
    {
        var scale = Math.Min(1d, Math.Max(64, maxDimension) / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var image = source.Clone(context => context.Resize(width, height));

        var pixelCount = width * height;
        var light = 0;
        var locallyFlat = 0;
        var edgeSamples = 0;
        var strongEdges = 0;
        var bins = new HashSet<int>();
        var luminance = new double[pixelCount];

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var p = row[x];
                    var value = (0.2126 * p.R) + (0.7152 * p.G) + (0.0722 * p.B);
                    luminance[(y * width) + x] = value;
                    if (value >= 225) light++;
                    bins.Add(((p.R >> 5) << 6) | ((p.G >> 5) << 3) | (p.B >> 5));
                }
            }
        });

        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var index = (y * width) + x;
                var gx = -luminance[index - width - 1] + luminance[index - width + 1]
                         - (2 * luminance[index - 1]) + (2 * luminance[index + 1])
                         - luminance[index + width - 1] + luminance[index + width + 1];
                var gy = -luminance[index - width - 1] - (2 * luminance[index - width]) - luminance[index - width + 1]
                         + luminance[index + width - 1] + (2 * luminance[index + width]) + luminance[index + width + 1];
                if (Math.Sqrt((gx * gx) + (gy * gy)) >= 150) strongEdges++;
                edgeSamples++;

                var centre = luminance[index];
                var localDifference = Math.Abs(centre - luminance[index - 1])
                                      + Math.Abs(centre - luminance[index + 1])
                                      + Math.Abs(centre - luminance[index - width])
                                      + Math.Abs(centre - luminance[index + width]);
                if (localDifference / 4d <= 7) locallyFlat++;
            }
        }

        return new VisualMetrics(
            light / (double)Math.Max(1, pixelCount),
            locallyFlat / (double)Math.Max(1, edgeSamples),
            strongEdges / (double)Math.Max(1, edgeSamples),
            bins.Count / 512d,
            source.Width / (double)Math.Max(1, source.Height));
    }

    private static MediaClassificationResult Result(MediaClassification classification, double confidence, string signal)
        => new(classification, confidence, new[] { signal }, ClassifierVersion);

    private static bool IsCommonScreenDimension(int width, int height)
    {
        var dimensions = new (int Width, int Height)[]
        {
            (1920, 1080), (1366, 768), (1280, 720), (1536, 864), (1600, 900),
            (2560, 1440), (3840, 2160), (1440, 900), (1680, 1050),
            (1170, 2532), (1179, 2556), (1242, 2688), (1284, 2778),
            (1080, 1920), (1080, 2400), (1080, 2340), (1440, 3200)
        };

        return dimensions.Any(candidate =>
            (Math.Abs(candidate.Width - width) <= 8 && Math.Abs(candidate.Height - height) <= 8)
            || (Math.Abs(candidate.Width - height) <= 8 && Math.Abs(candidate.Height - width) <= 8));
    }

    private sealed record VisualMetrics(
        double LightBackgroundRatio,
        double FlatPixelRatio,
        double EdgeDensity,
        double ColourDiversity,
        double WideAspectRatio);
}
