using System.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Conservative, offline hybrid classifier. Natural-photograph admission is deliberately
/// fail-closed: detector-only face evidence can support an existing photograph hypothesis,
/// but can never create that hypothesis or override document/diagram/graphic structure.
/// </summary>
public sealed class MediaClassifier : IMediaClassifier
{
    public const string ClassifierVersion = "hybrid-media-v7";

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

    private static readonly string[] ScreenshotTerms =
        { "screenshot", "screen-shot", "screen_shot", "snip", "screen capture" };
    private static readonly string[] DiagramTerms =
        { "drawio", "flow_chart", "flow-chart", "flowchart", "workflow", "architecture", "schematic", "block-diagram" };
    private static readonly string[] ChartTerms =
        { "chart", "graph", "category-share", "stage-cycle", "dashboard-export", "by-current-stage", "by-project-cost-band" };
    private static readonly string[] DocumentTerms =
        { "scan", "scanned", "worksheet", "question-paper", "certificate", "letter", "document", "form" };
    private static readonly string[] PresentationTerms =
        { "slide", "presentation", "powerpoint", "ppt", "deck" };
    private static readonly string[] GraphicTerms =
        { "logo", "icon", "banner", "poster", "illustration", "wallpaper", "clipart", "template", "border", "background" };

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
        var profile = AnalyseStructure(metrics);
        var evidence = CreateEvidenceMap();
        var signals = new List<string>();
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var filenameFlags = DetectFilenameEvidence(name);

        ApplyMetadataEvidence(metadata, extension, evidence, signals);
        ApplyFilenameEvidence(filenameFlags, evidence, signals);
        ApplyPixelEvidence(metrics, profile, evidence, signals);
        DisableConfiguredCategories(evidence);

        // Capture the pre-face state. Decision safety is evaluated against this immutable
        // baseline so a face boost cannot erase contradictory document or graphic evidence.
        var rawEvidence = Snapshot(evidence);
        var baseScores = Softmax(rawEvidence, temperature: 0.82);
        var safety = AssessSafety(metadata, filenameFlags, metrics, profile, baseScores);

        FacePresenceResult? facePresence = null;
        var faceProbeAttempted = false;
        var faceEvidenceDetected = false;
        var faceEvidenceUsed = false;
        string? faceEvidenceDecisionCode = null;

        if (_options.Classification.FacePresenceAssistanceEnabled
            && ShouldProbeForFace(rawEvidence, baseScores))
        {
            faceProbeAttempted = true;
            facePresence = await _faceProbe.AnalyseAsync(bytes, cancellationToken);
            if (IsVerifiedFace(facePresence))
            {
                faceEvidenceDetected = true;
                if (safety.NaturalPhotoBaselineSatisfied && !safety.HasStructuralVeto)
                {
                    var confidenceFactor = 0.75 + 0.25 * Clamp01(facePresence.HighestConfidence);
                    var boundedBoost = Math.Min(
                        _options.Classification.FacePresenceEvidenceBoost,
                        _options.Classification.FacePresenceEvidenceBoost * confidenceFactor);
                    evidence[MediaClassification.Photograph] += boundedBoost;
                    evidence[MediaClassification.Unknown] -= _options.Classification.FacePresenceUnknownReduction;
                    faceEvidenceUsed = true;
                    faceEvidenceDecisionCode = "FACE_EVIDENCE_USED_AS_SUPPORT";
                    signals.Add(
                        $"Verified detector-only face evidence supported an existing natural-photograph baseline " +
                        $"({facePresence.HighestConfidence:P0}, area {facePresence.LargestFaceAreaRatio:P1}).");
                }
                else
                {
                    faceEvidenceDecisionCode = safety.HasStructuralVeto
                        ? "FACE_EVIDENCE_REJECTED_BY_STRUCTURE"
                        : "FACE_EVIDENCE_REJECTED_BY_BASELINE";
                    signals.Add(
                        "A face-like structure was detected, but it was not used because the image did not pass the natural-photograph safety gate.");
                }
            }
            else if (facePresence is { Succeeded: false })
            {
                faceEvidenceDecisionCode = "FACE_PROBE_UNAVAILABLE";
                signals.Add("Optional face-presence assistance was unavailable; classification continued conservatively.");
            }
            else
            {
                faceEvidenceDecisionCode = "NO_VERIFIED_FACE_EVIDENCE";
            }
        }

        safety = safety with
        {
            FaceProbeAttempted = faceProbeAttempted,
            FaceEvidenceDetected = faceEvidenceDetected,
            FaceEvidenceUsed = faceEvidenceUsed,
            FaceEvidenceDecisionCode = faceEvidenceDecisionCode
        };

        var normalized = Softmax(evidence, temperature: 0.82);
        var winner = normalized.OrderByDescending(pair => pair.Value).First();
        var decision = _decisionPolicy.Decide(
            winner.Key,
            winner.Value,
            new MediaClassificationDecisionContext(
                normalized,
                baseScores,
                rawEvidence,
                safety,
                signals));

        stopwatch.Stop();
        return new MediaClassificationResult(
            winner.Key,
            winner.Value,
            normalized,
            baseScores,
            rawEvidence,
            signals,
            metrics,
            safety,
            facePresence,
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
            // File type is only weak evidence; graphics and documents are frequently exported as JPEG.
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
        FilenameEvidence flags,
        IDictionary<MediaClassification, double> evidence,
        ICollection<string> signals)
    {
        if (_options.Classification.ScreenshotDetectionEnabled && flags.Screenshot)
        {
            AddEvidence(MediaClassification.Screenshot, 5.25, evidence, signals,
                "Explicit screenshot filename evidence.");
        }

        if (_options.Classification.DiagramDetectionEnabled && flags.Diagram)
        {
            AddEvidence(MediaClassification.Diagram, flags.StrongDiagram ? 5.75 : 4.25, evidence, signals,
                flags.StrongDiagram
                    ? "Explicit diagram or workflow filename evidence."
                    : "Chart or graph filename evidence.");
        }

        if (_options.Classification.DocumentDetectionEnabled && flags.Document)
        {
            AddEvidence(MediaClassification.ScannedDocument, 4.50, evidence, signals,
                "Document or worksheet filename evidence.");
        }

        if (_options.Classification.DocumentDetectionEnabled && flags.Presentation)
        {
            AddEvidence(MediaClassification.PresentationSlide, 4.75, evidence, signals,
                "Presentation filename evidence.");
        }

        if (flags.Graphic)
        {
            AddEvidence(MediaClassification.Graphic, 4.25, evidence, signals,
                "Graphic-design filename evidence.");
        }
    }

    private static void ApplyPixelEvidence(
        ClassificationMetrics metrics,
        StructuralProfile profile,
        IDictionary<MediaClassification, double> evidence,
        ICollection<string> signals)
    {
        evidence[MediaClassification.Photograph] += profile.ContinuousTone * 4.80
                                                     + profile.NaturalPhoto * 1.50
                                                     - profile.PageStructure * 4.50
                                                     - profile.DesignedGraphic * 3.20
                                                     - profile.DiagramStructure * 2.00;
        evidence[MediaClassification.ScannedDocument] += profile.PageStructure * 5.00
                                                         + profile.WhiteCanvas * 0.80
                                                         + metrics.TextRowRatio * 1.20;
        evidence[MediaClassification.Diagram] += profile.DiagramStructure * 4.20
                                                + profile.WhiteCanvas * 1.40
                                                + metrics.TextColumnRatio * 0.50;
        evidence[MediaClassification.Graphic] += profile.DesignedGraphic * 4.50
                                                + metrics.DominantPaletteRatio * 0.25;

        if (profile.ContinuousTone >= 0.58)
        {
            signals.Add("Continuous-tone texture and colour variation support a natural photograph.");
        }
        if (profile.PageStructure >= 0.30)
        {
            signals.Add("Page-like background and repeated text-row structure support document content.");
        }
        if (profile.DiagramStructure >= 0.40 || profile.WhiteCanvas >= 0.42)
        {
            signals.Add("Structured content on a flat canvas supports diagram or chart content.");
        }
        if (profile.DesignedGraphic >= 0.50)
        {
            signals.Add("Digitally flat colour regions and palette concentration support designed graphic content.");
        }
        if (profile.NaturalPhoto < 0.22)
        {
            signals.Add("Natural-photograph structure is weak.");
        }
    }

    private ClassificationSafetyAssessment AssessSafety(
        MediaFileMetadata metadata,
        FilenameEvidence filename,
        ClassificationMetrics metrics,
        StructuralProfile profile,
        IReadOnlyDictionary<MediaClassification, double> baseScores)
    {
        var documentVeto = filename.Document
                           || filename.Presentation
                           || profile.PageStructure >= _options.Classification.DocumentStructureVetoThreshold
                           || (metrics.TextRowRatio >= 0.18 && metrics.LightBackgroundRatio >= 0.55)
                           || (metrics.DenseTextRowRatio >= 0.09 && metrics.BorderLightRatio >= 0.72);
        var graphicVeto = filename.Graphic
                          || (profile.DesignedGraphic >= _options.Classification.GraphicStructureVetoThreshold
                              && profile.NaturalPhoto < 0.58);
        var diagramVeto = filename.Diagram
                          || (profile.DiagramStructure >= _options.Classification.DiagramStructureVetoThreshold
                              && profile.NaturalPhoto < 0.58);
        var explicitNonPhoto = filename.HasNonPhotoEvidence;
        var basePhotoScore = Score(baseScores, MediaClassification.Photograph);
        var strongestBaseNonPhoto = baseScores
            .Where(pair => pair.Key is not MediaClassification.Unknown and not MediaClassification.Photograph)
            .Select(pair => pair.Value)
            .DefaultIfEmpty(0d)
            .Max();
        var naturalBaseline = (metadata.HasCameraMetadata
                               || profile.NaturalPhoto >= _options.Classification.NaturalPhotoBaselineMinimumScore)
                              && basePhotoScore >= _options.Classification.FaceProbeBasePhotographMinimumScore;

        return new ClassificationSafetyAssessment(
            naturalBaseline,
            documentVeto,
            graphicVeto,
            diagramVeto,
            explicitNonPhoto,
            profile.NaturalPhoto,
            profile.PageStructure,
            profile.DesignedGraphic,
            profile.DiagramStructure,
            basePhotoScore,
            strongestBaseNonPhoto,
            false,
            false,
            false,
            null);
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

    private bool ShouldProbeForFace(
        IReadOnlyDictionary<MediaClassification, double> evidence,
        IReadOnlyDictionary<MediaClassification, double> baseScores)
    {
        if (Score(baseScores, MediaClassification.Photograph)
            < _options.Classification.FaceProbeBasePhotographMinimumScore)
        {
            return false;
        }

        var best = evidence
            .Where(pair => double.IsFinite(pair.Value))
            .OrderByDescending(pair => pair.Value)
            .First();
        return best.Key is MediaClassification.Photograph or MediaClassification.Unknown
               || evidence[MediaClassification.Photograph] >= best.Value - 1.75;
    }

    private bool IsVerifiedFace(FacePresenceResult? face)
        => face is
           {
               Succeeded: true,
               FaceDetected: true,
               ValidFivePointLandmarks: true
           }
           && face.HighestConfidence >= _options.Classification.FacePresenceMinimumConfidence
           && face.LargestFaceWidth >= _options.Classification.FacePresenceMinimumPixels
           && face.LargestFaceHeight >= _options.Classification.FacePresenceMinimumPixels
           && face.LargestFaceAreaRatio >= _options.Classification.FacePresenceMinimumAreaRatio;

    private static FilenameEvidence DetectFilenameEvidence(string name)
    {
        var strongDiagram = ContainsAnyKeyword(name, DiagramTerms);
        var chart = ContainsAnyKeyword(name, ChartTerms);
        return new FilenameEvidence(
            ContainsAnyKeyword(name, ScreenshotTerms),
            ContainsAnyKeyword(name, DocumentTerms),
            strongDiagram || chart,
            strongDiagram,
            ContainsAnyKeyword(name, PresentationTerms),
            ContainsAnyKeyword(name, GraphicTerms));
    }

    private static void AddEvidence(
        MediaClassification category,
        double weight,
        IDictionary<MediaClassification, double> evidence,
        ICollection<string> signals,
        string signal)
    {
        evidence[category] += weight;
        evidence[MediaClassification.Unknown] -= 0.65;
        signals.Add(signal);
    }

    private static bool ContainsAnyKeyword(string name, IEnumerable<string> terms)
        => terms.Any(term => ContainsKeyword(name, term));

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

    private static IReadOnlyDictionary<MediaClassification, double> Snapshot(
        IReadOnlyDictionary<MediaClassification, double> evidence)
        => ScoredCategories.ToDictionary(
            category => category,
            category => evidence.TryGetValue(category, out var value) ? value : double.NegativeInfinity);

    private static IReadOnlyDictionary<MediaClassification, double> Softmax(
        IReadOnlyDictionary<MediaClassification, double> evidence,
        double temperature)
    {
        var finite = evidence.Where(pair => double.IsFinite(pair.Value)).ToArray();
        if (finite.Length == 0)
        {
            return ScoredCategories.ToDictionary(category => category, _ => 0d);
        }

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

    private static StructuralProfile AnalyseStructure(ClassificationMetrics metrics)
    {
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

        var pageStructure = Clamp01((metrics.LightBackgroundRatio - 0.55) / 0.40)
                            * (0.30 * Clamp01((metrics.TextRowRatio - 0.06) / 0.34)
                               + 0.20 * Clamp01((metrics.TextColumnRatio - 0.08) / 0.65)
                               + 0.18 * Clamp01((metrics.BorderLightRatio - 0.65) / 0.35)
                               + 0.16 * Clamp01((metrics.InkCoverage - 0.02) / 0.20)
                               + 0.16 * Clamp01((metrics.EdgeDensity - 0.008) / 0.10));

        var designedGraphic = 0.35 * Clamp01((metrics.ExactFlatness - 0.70) / 0.28)
                              + 0.25 * Clamp01((metrics.DominantPaletteRatio - 0.78) / 0.20)
                              + 0.20 * Clamp01((0.22 - metrics.MicroVariationRatio) / 0.20)
                              + 0.20 * Clamp01((metrics.MeanSaturation - 0.08) / 0.35);

        var diagramStructure = Clamp01((metrics.LightBackgroundRatio - 0.55) / 0.40)
                               * (0.34 * Clamp01((metrics.TextColumnRatio - 0.15) / 0.70)
                                  + 0.25 * Clamp01((metrics.TextRowRatio - 0.08) / 0.35)
                                  + 0.22 * Clamp01((metrics.ExactFlatness - 0.65) / 0.32)
                                  + 0.19 * Clamp01((metrics.EdgeDensity - 0.012) / 0.12));

        var naturalPhoto = 0.38 * continuousTone
                           + 0.20 * Clamp01((0.78 - metrics.ExactFlatness) / 0.45)
                           + 0.18 * Clamp01((metrics.MicroVariationRatio - 0.12) / 0.45)
                           + 0.14 * Clamp01((metrics.LuminanceVariance - 0.01) / 0.08)
                           + 0.10 * Clamp01((0.86 - metrics.BorderLightRatio) / 0.50);

        return new StructuralProfile(
            continuousTone,
            whiteCanvas,
            pageStructure,
            designedGraphic,
            diagramStructure,
            naturalPhoto);
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
        var luminance = new double[pixels];
        var dark = new bool[pixels];
        double luminanceSum = 0;
        double luminanceSquareSum = 0;
        double saturationSum = 0;
        double saturationSquareSum = 0;
        double edgeCount = 0;
        double flatCount = 0;
        double exactFlatCount = 0;
        double microVariationCount = 0;
        double lightCount = 0;
        double darkCount = 0;
        var histogram = new int[32];
        var colours = new HashSet<int>();
        var palette = new int[512];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var pixel = image[x, y];
                var pixelLuminance = (.2126 * pixel.R + .7152 * pixel.G + .0722 * pixel.B) / 255d;
                luminance[index] = pixelLuminance;
                dark[index] = pixelLuminance < 0.72;
                luminanceSum += pixelLuminance;
                luminanceSquareSum += pixelLuminance * pixelLuminance;
                histogram[Math.Min(31, (int)(pixelLuminance * 32))]++;
                if (pixelLuminance > .86) lightCount++;
                if (dark[index]) darkCount++;

                var maximum = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                var minimum = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                var saturation = maximum == 0 ? 0d : (maximum - minimum) / (double)maximum;
                saturationSum += saturation;
                saturationSquareSum += saturation * saturation;

                var quantized = (pixel.R / 32) * 64 + (pixel.G / 32) * 8 + pixel.B / 32;
                colours.Add(quantized);
                palette[quantized]++;

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

        var horizontalTransitions = 0;
        var verticalTransitions = 0;
        var textRows = 0;
        var denseTextRows = 0;
        for (var y = 0; y < height; y++)
        {
            var rowDark = 0;
            var rowTransitions = 0;
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (dark[index]) rowDark++;
                if (x > 0 && dark[index] != dark[index - 1]) rowTransitions++;
            }

            horizontalTransitions += rowTransitions;
            var darkRatio = rowDark / (double)Math.Max(1, width);
            var transitionRatio = rowTransitions / (double)Math.Max(1, width - 1);
            if (darkRatio is >= 0.008 and <= 0.48 && transitionRatio >= 0.025) textRows++;
            if (darkRatio is >= 0.02 and <= 0.42 && transitionRatio >= 0.05) denseTextRows++;
        }

        var textColumns = 0;
        for (var x = 0; x < width; x++)
        {
            var columnDark = 0;
            var columnTransitions = 0;
            for (var y = 0; y < height; y++)
            {
                var index = y * width + x;
                if (dark[index]) columnDark++;
                if (y > 0 && dark[index] != dark[index - width]) columnTransitions++;
            }

            verticalTransitions += columnTransitions;
            var darkRatio = columnDark / (double)Math.Max(1, height);
            var transitionRatio = columnTransitions / (double)Math.Max(1, height - 1);
            if (darkRatio is >= 0.008 and <= 0.55 && transitionRatio >= 0.025) textColumns++;
        }

        var borderThickness = Math.Max(1, (int)Math.Round(Math.Min(width, height) * 0.06));
        var borderLight = 0;
        var borderPixels = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= borderThickness && x < width - borderThickness
                    && y >= borderThickness && y < height - borderThickness)
                {
                    continue;
                }

                borderPixels++;
                if (luminance[y * width + x] > 0.86) borderLight++;
            }
        }

        var comparisons = Math.Max(1, (width - 1) * height + width * (height - 1));
        var horizontalComparisons = Math.Max(1, (width - 1) * height);
        var verticalComparisons = Math.Max(1, width * (height - 1));
        var mean = luminanceSum / pixels;
        var meanSaturation = saturationSum / pixels;
        var dominantPalette = palette
            .OrderByDescending(count => count)
            .Take(8)
            .Sum() / (double)pixels;

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
            height,
            darkCount / pixels,
            borderPixels == 0 ? 0 : borderLight / (double)borderPixels,
            textRows / (double)Math.Max(1, height),
            denseTextRows / (double)Math.Max(1, height),
            textColumns / (double)Math.Max(1, width),
            horizontalTransitions / (double)horizontalComparisons,
            verticalTransitions / (double)verticalComparisons,
            meanSaturation,
            Math.Max(0, saturationSquareSum / pixels - meanSaturation * meanSaturation),
            dominantPalette);
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

    private static double Score(
        IReadOnlyDictionary<MediaClassification, double> scores,
        MediaClassification classification)
        => scores.TryGetValue(classification, out var value) ? value : 0d;

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    private static MediaClassificationResult Empty(string signal, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var metrics = new ClassificationMetrics(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var scores = ScoredCategories.ToDictionary(category => category, _ => 0d);
        var safety = new ClassificationSafetyAssessment(
            false, false, false, false, false,
            0, 0, 0, 0, 0, 0,
            false, false, false, null);
        return new MediaClassificationResult(
            MediaClassification.Unknown,
            0,
            scores,
            scores,
            scores,
            new[] { signal },
            metrics,
            safety,
            null,
            MediaClassification.Unknown,
            MediaClassificationDecisionStatus.NotApplicable,
            "NOT_APPLICABLE",
            ClassifierVersion,
            checked((int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds)));
    }

    private sealed record FilenameEvidence(
        bool Screenshot,
        bool Document,
        bool Diagram,
        bool StrongDiagram,
        bool Presentation,
        bool Graphic)
    {
        public bool HasNonPhotoEvidence => Screenshot || Document || Diagram || Presentation || Graphic;
    }

    private sealed record StructuralProfile(
        double ContinuousTone,
        double WhiteCanvas,
        double PageStructure,
        double DesignedGraphic,
        double DiagramStructure,
        double NaturalPhoto);
}
