using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaClassifier : IMediaClassifier
{
    public const string ClassifierVersion = "heuristic-screenshot-v1";

    private static readonly string[] ScreenshotTerms =
    {
        "screenshot", "screen shot", "screen-shot", "screen_capture", "screen capture",
        "snipping", "snip", "capture", "clipboard"
    };

    public Task<MediaClassificationResult> ClassifyAsync(
        string path,
        MediaFileMetadata metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (metadata.Kind == MediaAssetKind.Video)
        {
            return Task.FromResult(new MediaClassificationResult(
                MediaClassification.Unknown,
                0,
                new[] { "Video classification is not enabled in this release." },
                ClassifierVersion));
        }

        var signals = new List<string>();
        var screenshotScore = 0d;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        if (ScreenshotTerms.Any(term => fileName.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            screenshotScore += 0.60;
            signals.Add("Filename indicates a screenshot or screen capture.");
        }

        if (!metadata.HasCameraMetadata)
        {
            screenshotScore += 0.15;
            signals.Add("No camera make or model metadata is present.");
        }
        else
        {
            screenshotScore -= 0.45;
            signals.Add("Camera metadata is present.");
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            screenshotScore += 0.12;
            signals.Add("PNG is commonly used for screenshots.");
        }

        if (metadata.Width.HasValue && metadata.Height.HasValue)
        {
            var width = metadata.Width.Value;
            var height = metadata.Height.Value;
            var longSide = Math.Max(width, height);
            var shortSide = Math.Min(width, height);
            var ratio = shortSide > 0 ? (double)longSide / shortSide : 0;

            if (IsCommonScreenDimension(width, height))
            {
                screenshotScore += 0.22;
                signals.Add("Dimensions match a common display or mobile screenshot size.");
            }
            else if (longSide >= 1000 && ratio is >= 1.55 and <= 2.30 && !metadata.HasCameraMetadata)
            {
                screenshotScore += 0.08;
                signals.Add("Aspect ratio is consistent with a screen capture.");
            }
        }

        screenshotScore = Math.Clamp(screenshotScore, 0, 1);
        var classification = screenshotScore >= 0.62
            ? MediaClassification.Screenshot
            : MediaClassification.Photograph;

        var confidence = classification == MediaClassification.Screenshot
            ? screenshotScore
            : Math.Clamp(1 - screenshotScore, 0.50, 0.98);

        if (classification == MediaClassification.Photograph && metadata.HasCameraMetadata)
        {
            confidence = Math.Max(confidence, 0.94);
        }

        return Task.FromResult(new MediaClassificationResult(
            classification,
            confidence,
            signals,
            ClassifierVersion));
    }

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
}
