using SkiaSharp;

namespace ProjectManagement.Features.MediaLibrary.Services;

public readonly record struct FaceRectangle(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public double Area => Math.Max(0, Width) * Math.Max(0, Height);
}

public readonly record struct FacePoint(double X, double Y);

public readonly record struct SimilarityTransform(
    double A,
    double B,
    double TranslateX,
    double TranslateY)
{
    public FacePoint Map(FacePoint point)
        => new(
            A * point.X - B * point.Y + TranslateX,
            B * point.X + A * point.Y + TranslateY);

    public SKMatrix ToSkMatrix()
        => new()
        {
            ScaleX = (float)A,
            SkewX = (float)-B,
            TransX = (float)TranslateX,
            SkewY = (float)B,
            ScaleY = (float)A,
            TransY = (float)TranslateY,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };
}

public static class FaceGeometry
{
    public static double IntersectionOverUnion(FaceRectangle first, FaceRectangle second)
    {
        var intersectionLeft = Math.Max(first.Left, second.Left);
        var intersectionTop = Math.Max(first.Top, second.Top);
        var intersectionRight = Math.Min(first.Right, second.Right);
        var intersectionBottom = Math.Min(first.Bottom, second.Bottom);
        var width = Math.Max(0, intersectionRight - intersectionLeft);
        var height = Math.Max(0, intersectionBottom - intersectionTop);
        var intersection = width * height;
        var union = first.Area + second.Area - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    public static IReadOnlyList<T> NonMaximumSuppression<T>(
        IEnumerable<T> candidates,
        Func<T, FaceRectangle> rectangleSelector,
        Func<T, double> scoreSelector,
        double threshold,
        int maximumResults)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rectangleSelector);
        ArgumentNullException.ThrowIfNull(scoreSelector);

        var resultLimit = Math.Max(1, maximumResults);
        threshold = Math.Clamp(threshold, 0, 1);
        var remaining = candidates
            .OrderByDescending(scoreSelector)
            .ToList();
        var selected = new List<T>(Math.Min(resultLimit, remaining.Count));

        while (remaining.Count > 0 && selected.Count < resultLimit)
        {
            var current = remaining[0];
            remaining.RemoveAt(0);
            selected.Add(current);
            var currentRectangle = rectangleSelector(current);
            remaining.RemoveAll(candidate =>
                IntersectionOverUnion(currentRectangle, rectangleSelector(candidate)) > threshold);
        }

        return selected;
    }

    /// <summary>
    /// Fits the least-squares, rotation-preserving similarity transform that maps the
    /// source landmarks to the destination landmarks. Reflection is intentionally disallowed.
    /// </summary>
    public static bool TryCreateSimilarityTransform(
        IReadOnlyList<FacePoint> source,
        IReadOnlyList<FacePoint> destination,
        out SimilarityTransform transform)
    {
        transform = default;
        if (source.Count != destination.Count || source.Count < 2)
        {
            return false;
        }

        var sourceMeanX = source.Average(point => point.X);
        var sourceMeanY = source.Average(point => point.Y);
        var destinationMeanX = destination.Average(point => point.X);
        var destinationMeanY = destination.Average(point => point.Y);

        double denominator = 0;
        double aNumerator = 0;
        double bNumerator = 0;

        for (var index = 0; index < source.Count; index++)
        {
            var sourceX = source[index].X - sourceMeanX;
            var sourceY = source[index].Y - sourceMeanY;
            var destinationX = destination[index].X - destinationMeanX;
            var destinationY = destination[index].Y - destinationMeanY;

            denominator += sourceX * sourceX + sourceY * sourceY;
            aNumerator += sourceX * destinationX + sourceY * destinationY;
            bNumerator += sourceX * destinationY - sourceY * destinationX;
        }

        if (denominator <= 1e-9)
        {
            return false;
        }

        var a = aNumerator / denominator;
        var b = bNumerator / denominator;
        var translateX = destinationMeanX - a * sourceMeanX + b * sourceMeanY;
        var translateY = destinationMeanY - b * sourceMeanX - a * sourceMeanY;

        if (!double.IsFinite(a)
            || !double.IsFinite(b)
            || !double.IsFinite(translateX)
            || !double.IsFinite(translateY))
        {
            return false;
        }

        transform = new SimilarityTransform(a, b, translateX, translateY);
        return true;
    }

    public static SKRectI ClampRectangle(FaceRectangle rectangle, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp((int)Math.Floor(rectangle.Left), 0, Math.Max(0, imageWidth - 1));
        var top = Math.Clamp((int)Math.Floor(rectangle.Top), 0, Math.Max(0, imageHeight - 1));
        var right = Math.Clamp((int)Math.Ceiling(rectangle.Right), left + 1, imageWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(rectangle.Bottom), top + 1, imageHeight);
        return new SKRectI(left, top, right, bottom);
    }
}
