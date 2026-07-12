using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SkiaSharp;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record FaceQualityAssessment(
    double Score,
    FaceQualityStatus Status,
    FaceQualitySignals Signals);

public static class FaceQualityEvaluator
{
    public static FaceQualityAssessment Evaluate(
        SKBitmap image,
        SKRectI faceRectangle,
        IReadOnlyList<FacePoint>? landmarks,
        MediaPeopleOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);

        var reasons = new List<string>();
        var minimumDimension = Math.Min(faceRectangle.Width, faceRectangle.Height);
        var resolution = Math.Clamp(
            (minimumDimension - options.MinimumFacePixels) / (double)Math.Max(options.MinimumFacePixels * 2, 1),
            0,
            1);

        var statistics = CalculateLuminanceStatistics(image, faceRectangle);
        var sharpness = NormalizeSharpness(statistics.LaplacianVariance);
        var exposure = NormalizeExposure(statistics.Mean);
        var contrast = Math.Clamp(statistics.StandardDeviation / 56.0, 0, 1);
        var pose = CalculatePoseScore(faceRectangle, landmarks);
        var cropCompleteness = CalculateCropCompleteness(image, faceRectangle);

        if (minimumDimension < options.MinimumFacePixels)
            reasons.Add("Face resolution is below the configured minimum.");
        if (sharpness < 0.35)
            reasons.Add("The face is blurred or lacks sufficient detail.");
        if (exposure < 0.35)
            reasons.Add("The face is underexposed or overexposed.");
        if (contrast < 0.25)
            reasons.Add("The face has insufficient tonal contrast.");
        if (pose < 0.35)
            reasons.Add("The face pose is too oblique for reliable matching.");
        if (cropCompleteness < 0.65)
            reasons.Add("The face is too close to the image boundary.");

        // Resolution and sharpness carry the highest weight because an embedding cannot
        // recover detail that was never captured. The score remains explainable and bounded.
        var score = Math.Clamp(
            resolution * 0.25
            + sharpness * 0.25
            + exposure * 0.15
            + contrast * 0.10
            + pose * 0.15
            + cropCompleteness * 0.10,
            0,
            1);

        var status = ResolveStatus(
            minimumDimension,
            options.MinimumFacePixels,
            sharpness,
            exposure,
            pose,
            cropCompleteness,
            score,
            options.MinimumQualityScore);

        return new FaceQualityAssessment(
            score,
            status,
            new FaceQualitySignals(
                resolution,
                sharpness,
                exposure,
                contrast,
                pose,
                cropCompleteness,
                reasons));
    }

    private static FaceQualityStatus ResolveStatus(
        int minimumDimension,
        int minimumFacePixels,
        double sharpness,
        double exposure,
        double pose,
        double cropCompleteness,
        double score,
        double minimumQualityScore)
    {
        if (minimumDimension < minimumFacePixels) return FaceQualityStatus.LowResolution;
        if (sharpness < 0.25) return FaceQualityStatus.Blurred;
        if (exposure < 0.25) return FaceQualityStatus.PoorExposure;
        if (pose < 0.25) return FaceQualityStatus.ExtremePose;
        if (cropCompleteness < 0.50) return FaceQualityStatus.Occluded;
        return score >= minimumQualityScore
            ? FaceQualityStatus.EmbeddingEligible
            : FaceQualityStatus.Detected;
    }

    private static (double Mean, double StandardDeviation, double LaplacianVariance)
        CalculateLuminanceStatistics(SKBitmap image, SKRectI rectangle)
    {
        const int maximumSamplesPerAxis = 96;
        var stepX = Math.Max(1, rectangle.Width / maximumSamplesPerAxis);
        var stepY = Math.Max(1, rectangle.Height / maximumSamplesPerAxis);
        var width = Math.Max(3, (rectangle.Width + stepX - 1) / stepX);
        var height = Math.Max(3, (rectangle.Height + stepY - 1) / stepY);
        var luminance = new double[height, width];

        double sum = 0;
        double sumSquares = 0;
        var count = 0;
        for (var row = 0; row < height; row++)
        {
            var y = Math.Min(rectangle.Bottom - 1, rectangle.Top + row * stepY);
            for (var column = 0; column < width; column++)
            {
                var x = Math.Min(rectangle.Right - 1, rectangle.Left + column * stepX);
                var color = image.GetPixel(x, y);
                var value = 0.2126 * color.Red + 0.7152 * color.Green + 0.0722 * color.Blue;
                luminance[row, column] = value;
                sum += value;
                sumSquares += value * value;
                count++;
            }
        }

        var mean = count == 0 ? 0 : sum / count;
        var variance = count == 0 ? 0 : Math.Max(0, sumSquares / count - mean * mean);

        double laplacianSum = 0;
        double laplacianSquares = 0;
        var laplacianCount = 0;
        for (var row = 1; row < height - 1; row++)
        {
            for (var column = 1; column < width - 1; column++)
            {
                var value = luminance[row - 1, column]
                            + luminance[row + 1, column]
                            + luminance[row, column - 1]
                            + luminance[row, column + 1]
                            - 4 * luminance[row, column];
                laplacianSum += value;
                laplacianSquares += value * value;
                laplacianCount++;
            }
        }

        var laplacianMean = laplacianCount == 0 ? 0 : laplacianSum / laplacianCount;
        var laplacianVariance = laplacianCount == 0
            ? 0
            : Math.Max(0, laplacianSquares / laplacianCount - laplacianMean * laplacianMean);
        return (mean, Math.Sqrt(variance), laplacianVariance);
    }

    private static double NormalizeSharpness(double laplacianVariance)
    {
        // Log scaling keeps the score useful across phone cameras, screenshots and scans.
        return Math.Clamp(Math.Log10(1 + laplacianVariance) / 3.2, 0, 1);
    }

    private static double NormalizeExposure(double mean)
    {
        var distanceFromMidpoint = Math.Abs(mean - 127.5) / 127.5;
        return Math.Clamp(1 - Math.Pow(distanceFromMidpoint, 1.5), 0, 1);
    }

    private static double CalculatePoseScore(SKRectI rectangle, IReadOnlyList<FacePoint>? landmarks)
    {
        if (landmarks is null || landmarks.Count < 5)
        {
            return 0.55;
        }

        var rightEye = landmarks[0];
        var leftEye = landmarks[1];
        var nose = landmarks[2];
        var rightMouth = landmarks[3];
        var leftMouth = landmarks[4];
        var eyeDistance = Distance(rightEye, leftEye);
        var mouthDistance = Distance(rightMouth, leftMouth);
        if (eyeDistance < 1 || mouthDistance < 1)
        {
            return 0;
        }

        var eyeMidpoint = Midpoint(rightEye, leftEye);
        var mouthMidpoint = Midpoint(rightMouth, leftMouth);
        var rollRadians = Math.Atan2(leftEye.Y - rightEye.Y, leftEye.X - rightEye.X);
        var rollScore = Math.Clamp(1 - Math.Abs(rollRadians) / (Math.PI / 3), 0, 1);

        var faceWidth = Math.Max(1, rectangle.Width);
        var yawOffset = Math.Abs(nose.X - eyeMidpoint.X) / faceWidth;
        var yawScore = Math.Clamp(1 - yawOffset / 0.22, 0, 1);

        var verticalSpan = Math.Max(1, mouthMidpoint.Y - eyeMidpoint.Y);
        var noseVerticalRatio = (nose.Y - eyeMidpoint.Y) / verticalSpan;
        var pitchScore = Math.Clamp(1 - Math.Abs(noseVerticalRatio - 0.52) / 0.52, 0, 1);
        var proportionScore = Math.Clamp(mouthDistance / eyeDistance, 0.45, 1.2) / 1.2;

        return Math.Clamp(
            rollScore * 0.30 + yawScore * 0.35 + pitchScore * 0.25 + proportionScore * 0.10,
            0,
            1);
    }

    private static double CalculateCropCompleteness(SKBitmap image, SKRectI rectangle)
    {
        var marginX = Math.Min(rectangle.Left, image.Width - rectangle.Right) / (double)Math.Max(1, rectangle.Width);
        var marginY = Math.Min(rectangle.Top, image.Height - rectangle.Bottom) / (double)Math.Max(1, rectangle.Height);
        return Math.Clamp(Math.Min(marginX / 0.08, marginY / 0.08), 0, 1);
    }

    private static FacePoint Midpoint(FacePoint first, FacePoint second)
        => new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static double Distance(FacePoint first, FacePoint second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
