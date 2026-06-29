using System.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Conservative, offline hybrid classifier. Deterministic filename, metadata and pixel
/// evidence is converted into a normalized probability distribution. Optional detector-only
/// face evidence may support Photograph without enabling identity or embedding processing.
/// </summary>
public sealed class MediaClassifier : IMediaClassifier
{
    public const string ClassifierVersion = "hybrid-media-v6";

    private static readonly MediaClassification[] ScoredCategories =
    {
        MediaClassification.Unknown,
        MediaClassification.Photograph,
        MediaClassification.Screenshot,
        MediaClassification.ScannedDocument,
        MediaClassification.Diagram,
        MediaClassification.PresentationSlide,
        MediaClassification.Graphic
    };

    private readonly MediaLibraryOptions _options;
    private readonly IFacePresenceProbe _faceProbe;
    private readonly IMediaClassificationDecisionPolicy _decisionPolicy;

    public MediaClassifier(
        IOptions<MediaLibraryOptions> options,
        IFacePresenceProbe faceProbe,
        IMediaClassificationDecisionPolicy decisionPolicy)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _faceProbe = faceProbe ?? throw new ArgumentNullException(nameof(faceProbe));
        _decisionPolicy = decisionPolicy ?? throw new ArgumentNullException(nameof(decisionPolicy));
    }

    public async Task<MediaClassificationResult> ClassifyAsync(
        string path,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ClassifyCoreAsync(Path.GetFileName(path), stream, metadata, cancellationToken);
    }

    public async Task<MediaClassificationResult> ClassifyAsync(
        MediaContentDescriptor content,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await using var stream = await content.OpenReadAsync(cancellationToken);
        return await ClassifyCoreAsync(content.FileName, stream, metadata, cancellationToken);
    }

    private async Task<MediaClassificationResult> ClassifyCoreAsync(
        string fileName,
        Stream stream,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!_options.Classification.Enabled || metadata.Kind != MediaAssetKind.Photo)
        {
            return Empty("Classification is disabled or not applicable.", stopwatch);
        }

        await using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        var bytes = copy.ToArray();
        using var image = Image.Load<Rgba32>(bytes);
        var metrics = Measure(image, _options.Classification.AnalysisMaxDimension);
        var evidence = CreateEvidenceMap();
        var signals = new List<string>();
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        ApplyMetadataEvidence(metadata, extension, evidence, signals);
        ApplyFilenameEvidence(name, evidence, signals);
        ApplyPixelEvidence(metrics, evidence, signals);
        DisableConfiguredCategories(evidence);

        if (_options.Classification.FacePresenceAssistanceEnabled
            && ShouldProbeForFace(evidence))
        {
            var face = await _faceProbe.AnalyseAsync(bytes, cancellationToken);
            if (face.Succeeded
                && face.FaceDetected
                && face.HighestConfidence >= _options.Classification.FacePresenceMinimumConfidence
                && face.LargestFaceWidth >= _options.Classification.FacePresenceMinimumPixels
                && face.LargestFaceHeight >= _options.Classification.FacePresenceMinimumPixels
                && face.LargestFaceAreaRatio >= _options.Classification.FacePresenceMinimumAreaRatio
                && face.ValidFivePointLandmarks)
            {
                evidence[MediaClassification.Photograph] += 5.25;
                evidence[MediaClassification.Unknown] -= 1.25;
                signals.Add(
                    $"Verified detector-only face evidence supports a photograph " +
                    $"({face.HighestConfidence:P0}, area {face.LargestFaceAreaRatio:P1}).");
            }
            else if (!face.Succeeded)
            {
                signals.Add("Optional face-presence assistance was unavailable; classification continued conservatively.");
            }
        }

        var normalized = Softmax(evidence, temperature: 0.82);
        var winner = normalized.OrderByDescending(pair => pair.Value).First();
        var decision = _decisionPolicy.Decide(
            winner.Key,
            winner.Value,
            normalized,
            signals);

        stopwatch.Stop();
        return new MediaClassificationResult(
            winner.Key,
            winner.Value,
            normalized,
            signals,
            metrics,
            decision.EffectiveClassification,
            decision.Status,
            decision.ReasonCode,
            ClassifierVersion,
            checked((int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds)));
    }

    private static Dictionary<MediaClassification, double> CreateEvidenceMap()
        => new()
        {
            [MediaClassification.Unknown] = 0.80,
            [MediaClassification.Photograph] = 0.35,
            [MediaClassification.Screenshot] = 0.10,
            [MediaClassification.ScannedDocument] = 0.10,
            [MediaClassification.Diagram] = 0.10,
            [MediaClassification.PresentationSlide] = 0.10,
            [MediaClassification.Graphic] = 0.10
        };

    private static void ApplyMetadataEvidence(
        MediaFileMetadata metadata,
        string extension,
        IDictionary<MediaClassification, double> evidence,
        ICollection<string> signals)
    {
        if (metadata.HasCameraMetadata)
        {
            evidence[MediaClassification.Photograph] += 3.25;
            evidence[MediaClassification.Unknown] -= 0.55;
            signals.Add("Camera metadata strongly supports a natural photograph.");
        }

        if (extension is ".jpg" or ".jpeg" or ".heic" or ".heif")
        {
            evidence[MediaClassification.Photograph] += 0.50;
        }
        else if (extension == ".png")
        {
            evidence[MediaClassification.Screenshot] += 0.20;
            evidence[MediaClassification.Diagram] += 0.15;
            evidence[MediaClassification.Graphic] += 0.15;
        }
    }

    private void ApplyFilenameEvidence(
        string name,
        IDictionary<MediaClassification, double> evidence,
        ICollection<string> signals)
    {
        if (_options.Classification.ScreenshotDetectionEnabled)
        {
            AddNameEvidence(name, new[] { "screenshot", "screen-shot", "screen_shot", "snip", "screen capture" },
                MediaClassification.Screenshot, 5.25, evidence, signals,
                "Explicit screenshot filename evidence.");
        }

        if (_options.Classification.DiagramDetectionEnabled)
        {
            AddNameEvidence(name, new[] { "drawio", "flow_chart", "flow-chart", "flowchart", "workflow", "architecture", "schematic", "block-diagram" },
                MediaClassification.Diagram, 5.75, evidence, signals,
                "Explicit diagram or workflow filename evidence.");
            AddNameEvidence(name, new[]
                {
                    "chart", "graph", "category-share", "stage-cycle", "dashboard-export",
                    "by-current-stage", "by-project-cost-band"
                },
                MediaClassification.Diagram, 4.25, evidence, signals,
                "Chart or graph filename evidence.");
        }

        if (_options.Classification.DocumentDetectionEnabled)
        {
            AddNameEvidence(name, new[] { "scan", "scanned", "worksheet", "question-paper", "certificate", "letter", "document", "form" },
                MediaClassification.ScannedDocument, 4.50, evidence, signals,
                "Document or worksheet filename evidence.");
            AddNameEvidence(name, new[] { "slide", "presentation", "powerpoint", "ppt", "deck" },
                MediaClassification.PresentationSlide, 4.75, evidence, signals,
                "Presentation filename evidence.");
        }

        AddNameEvidence(name, new[] { "logo", "icon", "banner", "poster", "illustration", "wallpaper", "clipart" },
            MediaClassification.Graphic, 4.25, evidence, signals,
            "Graphic-design filename evidence.");
    }

    private static void ApplyPixelEvidence(
        ClassificationMetrics metrics,
        IDictionary<MediaClassification, double> evidence,
        ICollection<string> signals)
    {
        // Natural photographs often contain continuous, low-amplitude variation even where
        // large areas look smooth to the eye. Exact-flat and micro-variation ratios separate
        // those regions from digitally filled backgrounds much better than a single flatness
        // threshold. The score remains conservative: difficult scenes are sent to review.
        var entropyStrength = Clamp01((metrics.Entropy - 0.32) / 0.52);
        var microVariationStrength = Clamp01((metrics.MicroVariationRatio - 0.10) / 0.55);
        var varianceStrength = Clamp01((metrics.LuminanceVariance - 0.006) / 0.065);
        var colourStrength = Clamp01((metrics.ColourDiversity - 0.13) / 0.52);
        var continuousTone = 0.30 * entropyStrength
                             + 0.30 * microVariationStrength
                             + 0.22 * varianceStrength
                             + 0.18 * colourStrength;

        var whiteCanvas = Clamp01((metrics.LightBackgroundRatio - 0.62) / 0.32)
                          * (0.42 * Clamp01((metrics.ExactFlatness - 0.50) / 0.45)
                             + 0.32 * Clamp01((metrics.ColourDiversity - 0.18) / 0.55)
                             + 0.26 * Clamp01((metrics.EdgeDensity - 0.008) / 0.10));
        var pageStructure = Clamp01((metrics.LightBackgroundRatio - 0.60) / 0.35)
                            * (0.50 * Clamp01((metrics.EdgeDensity - 0.01) / 0.12)
                               + 0.25 * Clamp01((metrics.ExactFlatness - 0.55) / 0.40)
                               + 0.25 * Clamp01((metrics.ColourDiversity - 0.15) / 0.60));
        var lineStructure = Clamp01((metrics.EdgeDensity - 0.06) / 0.22)
                            * Clamp01((0.70 - metrics.ColourDiversity) / 0.55);
        var designedFlatness = Clamp01((metrics.ExactFlatness - 0.72) / 0.25)
                               * Clamp01((0.20 - metrics.MicroVariationRatio) / 0.18);

        evidence[MediaClassification.Photograph] += continuousTone * 5.20
                                                     - whiteCanvas * 3.50
                                                     - designedFlatness * 3.40;
        evidence[MediaClassification.ScannedDocument] += pageStructure * 2.80 + whiteCanvas;
        evidence[MediaClassification.Diagram] += lineStructure * 2.40 + whiteCanvas * 2.20;
        evidence[MediaClassification.Graphic] += designedFlatness * 4.50;

        if (continuousTone >= 0.58)
        {
            signals.Add("Continuous-tone texture and colour variation support a photograph.");
        }
        if (pageStructure >= 0.45)
        {
            signals.Add("A light page-like background with foreground detail supports document content.");
        }
        if (lineStructure >= 0.45 || whiteCanvas >= 0.42)
        {
            signals.Add("Structured content on a flat canvas supports diagram or chart content.");
        }
        if (designedFlatness >= 0.50)
        {
            signals.Add("Digitally flat colour regions support designed graphic content.");
        }

        if (metrics.LightBackgroundRatio > 0.88 && metrics.ColourDiversity < 0.18)
        {
            evidence[MediaClassification.Photograph] -= 0.75;
            evidence[MediaClassification.ScannedDocument] += 0.65;
        }
    }

    private void DisableConfiguredCategories(IDictionary<MediaClassification, double> evidence)
    {
        if (!_options.Classification.ScreenshotDetectionEnabled)
        {
            evidence[MediaClassification.Screenshot] = double.NegativeInfinity;
        }
        if (!_options.Classification.DocumentDetectionEnabled)
        {
            evidence[MediaClassification.ScannedDocument] = double.NegativeInfinity;
            evidence[MediaClassification.PresentationSlide] = double.NegativeInfinity;
        }
        if (!_options.Classification.DiagramDetectionEnabled)
        {
            evidence[MediaClassification.Diagram] = double.NegativeInfinity;
        }
    }

    private static bool ShouldProbeForFace(IReadOnlyDictionary<MediaClassification, double> evidence)
    {
        var best = evidence.OrderByDescending(pair => pair.Value).First();
        return best.Key is MediaClassification.Photograph or MediaClassification.Unknown
               || evidence[MediaClassification.Photograph] >= best.Value - 1.75;
    }

    private static void AddNameEvidence(
        string name,
        IEnumerable<string> terms,
        MediaClassification category,
        double weight,
        IDictionary<MediaClassification, double> evidence,
        ICollection<string> signals,
        string signal)
    {
        if (!terms.Any(term => ContainsKeyword(name, term)))
        {
            return;
        }

        evidence[category] += weight;
        evidence[MediaClassification.Unknown] -= 0.65;
        signals.Add(signal);
    }

    private static bool ContainsKeyword(string name, string term)
    {
        var normalizedName = NormalizeForKeywordMatch(name);
        var normalizedTerm = NormalizeForKeywordMatch(term);
        return normalizedTerm.Length > 0
               && $" {normalizedName} ".Contains($" {normalizedTerm} ", StringComparison.Ordinal);
    }

    private static string NormalizeForKeywordMatch(string value)
    {
        var buffer = new char[value.Length];
        var length = 0;
        var separatorPending = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && length > 0)
                {
                    buffer[length++] = ' ';
                }

                buffer[length++] = char.ToLowerInvariant(character);
                separatorPending = false;
            }
            else
            {
                separatorPending = length > 0;
            }
        }

        return new string(buffer, 0, length);
    }

    private static IReadOnlyDictionary<MediaClassification, double> Softmax(
        IReadOnlyDictionary<MediaClassification, double> evidence,
        double temperature)
    {
        var finite = evidence.Where(pair => double.IsFinite(pair.Value)).ToArray();
        var maximum = finite.Max(pair => pair.Value);
        var exponentials = finite.ToDictionary(
            pair => pair.Key,
            pair => Math.Exp((pair.Value - maximum) / temperature));
        var sum = exponentials.Values.Sum();

        return ScoredCategories.ToDictionary(
            category => category,
            category => exponentials.TryGetValue(category, out var value) && sum > 0
                ? value / sum
                : 0d);
    }

    private static ClassificationMetrics Measure(Image<Rgba32> source, int maxDimension)
    {
        using var image = source.Clone(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxDimension, maxDimension)
        }));
        var width = image.Width;
        var height = image.Height;
        var pixels = Math.Max(1, width * height);
        double luminanceSum = 0;
        double luminanceSquareSum = 0;
        double edgeCount = 0;
        double flatCount = 0;
        double exactFlatCount = 0;
        double microVariationCount = 0;
        double lightCount = 0;
        var histogram = new int[32];
        var colours = new HashSet<int>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = image[x, y];
                var luminance = (.2126 * pixel.R + .7152 * pixel.G + .0722 * pixel.B) / 255d;
                luminanceSum += luminance;
                luminanceSquareSum += luminance * luminance;
                histogram[Math.Min(31, (int)(luminance * 32))]++;
                if (luminance > .86) lightCount++;
                colours.Add((pixel.R / 32) * 64 + (pixel.G / 32) * 8 + pixel.B / 32);

                if (x > 0)
                {
                    MeasureNeighbour(
                        pixel,
                        image[x - 1, y],
                        ref edgeCount,
                        ref flatCount,
                        ref exactFlatCount,
                        ref microVariationCount);
                }
                if (y > 0)
                {
                    MeasureNeighbour(
                        pixel,
                        image[x, y - 1],
                        ref edgeCount,
                        ref flatCount,
                        ref exactFlatCount,
                        ref microVariationCount);
                }
            }
        }

        double entropy = 0;
        foreach (var count in histogram)
        {
            if (count == 0) continue;
            var probability = count / (double)pixels;
            entropy -= probability * Math.Log2(probability);
        }
        entropy /= 5d;

        var comparisons = Math.Max(1, (width - 1) * height + width * (height - 1));
        var mean = luminanceSum / pixels;
        return new ClassificationMetrics(
            Math.Clamp(entropy, 0, 1),
            edgeCount / comparisons,
            flatCount / comparisons,
            exactFlatCount / comparisons,
            microVariationCount / comparisons,
            lightCount / pixels,
            Math.Min(1, colours.Count / 256d),
            Math.Max(0, luminanceSquareSum / pixels - mean * mean),
            width / (double)Math.Max(1, height),
            width,
            height);
    }

    private static void MeasureNeighbour(
        Rgba32 first,
        Rgba32 second,
        ref double edges,
        ref double flat,
        ref double exactFlat,
        ref double microVariation)
    {
        var difference = (Math.Abs(first.R - second.R)
                          + Math.Abs(first.G - second.G)
                          + Math.Abs(first.B - second.B)) / 765d;
        if (difference > .12) edges++;
        if (difference < .025) flat++;
        if (difference < .003) exactFlat++;
        if (difference is >= .003 and < .08) microVariation++;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    private static MediaClassificationResult Empty(string signal, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var metrics = new ClassificationMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var scores = ScoredCategories.ToDictionary(category => category, _ => 0d);
        return new MediaClassificationResult(
            MediaClassification.Unknown,
            0,
            scores,
            new[] { signal },
            metrics,
            MediaClassification.Unknown,
            MediaClassificationDecisionStatus.NotApplicable,
            "NOT_APPLICABLE",
            ClassifierVersion,
            checked((int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds)));
    }
}
