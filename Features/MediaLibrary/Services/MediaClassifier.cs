using System.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaClassifier : IMediaClassifier
{
    public const string ClassifierVersion = "hybrid-media-v5";
    private readonly MediaLibraryOptions _options;
    private readonly IFacePresenceProbe _faceProbe;
    private readonly IMediaClassificationDecisionPolicy _decisionPolicy;

    public MediaClassifier(IOptions<MediaLibraryOptions> options, IFacePresenceProbe faceProbe,
        IMediaClassificationDecisionPolicy decisionPolicy)
    { _options = options.Value; _faceProbe = faceProbe; _decisionPolicy = decisionPolicy; }

    public async Task<MediaClassificationResult> ClassifyAsync(string path, MediaFileMetadata metadata, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await ClassifyCoreAsync(Path.GetFileName(path), stream, metadata, ct);
    }

    public async Task<MediaClassificationResult> ClassifyAsync(MediaContentDescriptor content, MediaFileMetadata metadata, CancellationToken ct)
    {
        await using var stream = await content.OpenReadAsync(ct);
        return await ClassifyCoreAsync(content.FileName, stream, metadata, ct);
    }

    private async Task<MediaClassificationResult> ClassifyCoreAsync(string fileName, Stream stream, MediaFileMetadata metadata, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!_options.Classification.Enabled || metadata.Kind != MediaAssetKind.Photo)
            return Empty("Classification is disabled or not applicable.", sw);

        await using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        var bytes = copy.ToArray();
        using var image = Image.Load<Rgba32>(bytes);
        var metrics = Measure(image, _options.Classification.AnalysisMaxDimension);
        var scores = Enum.GetValues<MediaClassification>().ToDictionary(x => x, _ => 0d);
        var signals = new List<string>();
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        scores[MediaClassification.Photograph] = metadata.HasCameraMetadata ? .72 : .18;
        if (metadata.HasCameraMetadata) signals.Add("Camera metadata strongly supports a photograph.");
        if (ext is ".jpg" or ".jpeg" or ".heic" or ".heif" or ".webp") scores[MediaClassification.Photograph] += .10;

        AddNameScore(name, new[]{"screenshot","screen-shot","snip","capture"}, MediaClassification.Screenshot, .88, scores, signals, "Strong screenshot filename evidence.");
        AddNameScore(name, new[]{"drawio","flow_chart","flow-chart","flowchart","workflow","diagram","architecture","schematic"}, MediaClassification.Diagram, .95, scores, signals, "Strong diagram filename evidence.");
        AddNameScore(name, new[]{"chart","graph","category-share","stage-cycle"}, MediaClassification.Diagram, .78, scores, signals, "Chart or graph filename evidence.");
        AddNameScore(name, new[]{"scan","scanned","worksheet","form","certificate","letter"}, MediaClassification.ScannedDocument, .78, scores, signals, "Document or worksheet filename evidence.");
        AddNameScore(name, new[]{"slide","presentation","ppt"}, MediaClassification.PresentationSlide, .82, scores, signals, "Presentation filename evidence.");

        if (metrics.LightBackgroundRatio > .70 && metrics.EdgeDensity > .10) { scores[MediaClassification.ScannedDocument] += .36; signals.Add("Page-like light background with foreground detail."); }
        if (metrics.EdgeDensity > .22 && metrics.ColourDiversity < .35) { scores[MediaClassification.Diagram] += .35; signals.Add("Dense line structure supports diagram content."); }
        if (metrics.SpatialFlatness > .58) { scores[MediaClassification.Graphic] += .34; signals.Add("Large uniform regions support graphic content."); }
        if (metrics.Entropy > .50 && metrics.SpatialFlatness < .55 && metrics.EdgeDensity < .25) { scores[MediaClassification.Photograph] += .36; signals.Add("Natural continuous-tone texture supports a photograph."); }
        if (metrics.LuminanceVariance > .012 && metrics.ColourDiversity > .25) scores[MediaClassification.Photograph] += .18;

        var currentWinner = scores.OrderByDescending(x => x.Value).First();
        var ambiguousPhoto = currentWinner.Key is MediaClassification.Photograph or MediaClassification.Unknown
            || scores[MediaClassification.Photograph] >= .35;
        if (ambiguousPhoto && _options.Classification.FacePresenceAssistanceEnabled)
        {
            var face = await _faceProbe.AnalyseAsync(bytes, ct);
            if (face.Succeeded && face.FaceDetected
                && face.HighestConfidence >= _options.Classification.FacePresenceMinimumConfidence
                && face.LargestFaceWidth >= _options.Classification.FacePresenceMinimumPixels
                && face.LargestFaceHeight >= _options.Classification.FacePresenceMinimumPixels
                && face.ValidFivePointLandmarks)
            {
                scores[MediaClassification.Photograph] = Math.Max(scores[MediaClassification.Photograph], .94);
                signals.Add($"High-confidence natural face detected ({face.HighestConfidence:P0}) with valid landmarks.");
            }
            else if (!face.Succeeded) signals.Add("Optional face-presence probe was unavailable; classification continued conservatively.");
        }

        var normalized = scores.ToDictionary(x => x.Key, x => Math.Clamp(x.Value, 0, 1));
        var winner = normalized.OrderByDescending(x => x.Value).First();
        var decision = _decisionPolicy.Decide(winner.Key, winner.Value, normalized, signals);
        sw.Stop();
        return new(winner.Key, winner.Value, normalized, signals, metrics,
            decision.EffectiveClassification, decision.Status, decision.ReasonCode,
            ClassifierVersion, checked((int)Math.Min(int.MaxValue, sw.ElapsedMilliseconds)));
    }

    private static void AddNameScore(string name, IEnumerable<string> terms, MediaClassification category, double score,
        IDictionary<MediaClassification,double> scores, ICollection<string> signals, string signal)
    { if (terms.Any(name.Contains)) { scores[category] += score; signals.Add(signal); } }

    private static ClassificationMetrics Measure(Image<Rgba32> source, int maxDimension)
    {
        using var image = source.Clone(c => c.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maxDimension, maxDimension) }));
        var w=image.Width; var h=image.Height; var n=Math.Max(1,w*h); double sum=0,sum2=0,edges=0,flat=0,light=0; var hist=new int[32]; var colours=new HashSet<int>();
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) { var p=image[x,y]; var lum=(.2126*p.R+.7152*p.G+.0722*p.B)/255d; sum+=lum; sum2+=lum*lum; hist[Math.Min(31,(int)(lum*32))]++; if(lum>.86) light++; colours.Add((p.R/32)*64+(p.G/32)*8+p.B/32); if(x>0){var q=image[x-1,y]; var d=(Math.Abs(p.R-q.R)+Math.Abs(p.G-q.G)+Math.Abs(p.B-q.B))/765d; if(d>.12) edges++; if(d<.025) flat++;} if(y>0){var q=image[x,y-1]; var d=(Math.Abs(p.R-q.R)+Math.Abs(p.G-q.G)+Math.Abs(p.B-q.B))/765d; if(d>.12) edges++; if(d<.025) flat++;} }
        double entropy=0; foreach(var c in hist) if(c>0){var p=c/(double)n; entropy-=p*Math.Log2(p);} entropy/=5d;
        var comparisons=Math.Max(1,(w-1)*h+w*(h-1)); var mean=sum/n;
        return new(Math.Clamp(entropy,0,1), edges/comparisons, flat/comparisons, light/n,
            Math.Min(1,colours.Count/256d), Math.Max(0,sum2/n-mean*mean), w/(double)Math.Max(1,h), w,h);
    }

    private static MediaClassificationResult Empty(string signal, Stopwatch sw)
    { sw.Stop(); var metrics=new ClassificationMetrics(0,0,0,0,0,0,0,0,0); var scores=Enum.GetValues<MediaClassification>().ToDictionary(x=>x,_=>0d); return new(MediaClassification.Unknown,0,scores,new[]{signal},metrics,MediaClassification.Unknown,MediaClassificationDecisionStatus.NotApplicable,"NOT_APPLICABLE",ClassifierVersion,(int)sw.ElapsedMilliseconds); }
}
