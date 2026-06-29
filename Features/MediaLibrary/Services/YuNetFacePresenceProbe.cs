using ProjectManagement.Features.MediaLibrary.Options;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class YuNetFacePresenceProbe : IFacePresenceProbe
{
    private readonly IFaceAnalysisEngine _engine;
    private readonly MediaClassificationOptions _options;
    public YuNetFacePresenceProbe(IFaceAnalysisEngine engine, IOptions<MediaLibraryOptions> options)
    { _engine = engine; _options = options.Value.Classification; }

    public async Task<FacePresenceResult> AnalyseAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        if (!_options.FacePresenceAssistanceEnabled || imageBytes.Length == 0)
            return new(true, false, 0, 0, 0, 0, 0, false);
        try
        {
            var faces = await _engine.AnalyseAsync(imageBytes, cancellationToken);
            if (faces.Count == 0) return new(true, false, 0, 0, 0, 0, 0, false);
            var dimensions = Image.Identify(imageBytes);
            var sourceWidth = dimensions?.Width ?? 0;
            var sourceHeight = dimensions?.Height ?? 0;
            var best = faces.OrderByDescending(x => x.Confidence).First();
            var landmarksValid = best.Landmarks is { Count: >= 10 };
            return new(true, true, faces.Count, best.Confidence,
                (int)Math.Round(best.Width * sourceWidth), (int)Math.Round(best.Height * sourceHeight),
                best.Width * best.Height, landmarksValid);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return new(false, false, 0, 0, 0, 0, 0, false, ex.GetBaseException().Message); }
    }
}
