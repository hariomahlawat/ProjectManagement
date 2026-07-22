using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using SkiaSharp;

namespace ProjectManagement.Services.Ffc.Presentation;

public sealed class FfcPresentationMapRenderer : IFfcPresentationMapRenderer
{
    private static readonly SKColor[] ActiveBandColors =
    {
        SKColor.Parse("#D7DEFF"),
        SKColor.Parse("#B9C4F5"),
        SKColor.Parse("#8799E8"),
        SKColor.Parse("#4A63D8")
    };

    private static readonly SKPoint[] GenericLabelOffsets =
    {
        new(0, 0),
        new(0, -42),
        new(0, 42),
        new(54, 0),
        new(-54, 0),
        new(48, -34),
        new(-48, -34),
        new(48, 34),
        new(-48, 34),
        new(78, 0),
        new(-78, 0),
        new(70, -50),
        new(-70, -50),
        new(70, 50),
        new(-70, 50)
    };

    private static readonly IReadOnlyDictionary<string, SKPoint[]> PreferredLabelOffsets =
        new Dictionary<string, SKPoint[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FRA"] = new[] { new SKPoint(0, -38), new SKPoint(-42, -30) },
            ["NGA"] = new[] { new SKPoint(-48, -28), new SKPoint(42, -30) },
            ["ETH"] = new[] { new SKPoint(48, -28), new SKPoint(-45, -30) },
            ["TZA"] = new[] { new SKPoint(48, 28), new SKPoint(-44, 28) },
            ["MOZ"] = new[] { new SKPoint(48, 38), new SKPoint(-45, 38) },
            ["NAM"] = new[] { new SKPoint(-40, 30), new SKPoint(42, 30) },
            ["NPL"] = new[] { new SKPoint(-56, -34), new SKPoint(-72, -48) },
            ["BGD"] = new[] { new SKPoint(58, -34), new SKPoint(76, -46) },
            ["MMR"] = new[] { new SKPoint(62, 10), new SKPoint(72, 30) },
            ["LKA"] = new[] { new SKPoint(16, 58), new SKPoint(-32, 58) },
            ["KHM"] = new[] { new SKPoint(62, 36), new SKPoint(76, 48) }
        };

    private readonly Lazy<IReadOnlyList<MapFeature>> _features;

    public FfcPresentationMapRenderer(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var path = Path.Combine(
            environment.WebRootPath,
            "data",
            "maps",
            "world-countries-simplified.geojson");

        _features = new Lazy<IReadOnlyList<MapFeature>>(
            () => LoadFeatures(path),
            isThreadSafe: true);
    }

    public byte[] Render(
        IReadOnlyList<FfcPresentationCountry> countries,
        int width = 1800,
        int height = 1180)
    {
        ArgumentNullException.ThrowIfNull(countries);
        width = Math.Clamp(width, 1000, 3200);
        height = Math.Clamp(height, 520, 1800);

        var activeByIso = countries
            .Where(country => !string.IsNullOrWhiteSpace(country.IsoCode))
            .ToDictionary(
                country => country.IsoCode.Trim().ToUpperInvariant(),
                country => country,
                StringComparer.OrdinalIgnoreCase);

        var features = _features.Value;
        var activeFeatures = features
            .Where(feature => activeByIso.ContainsKey(feature.Iso3))
            .ToArray();

        var bounds = activeFeatures.Length == 0
            ? new GeoBounds(-25, -60, 120, 60)
            : GeoBounds.From(activeFeatures.SelectMany(feature => feature.AllPoints));
        bounds = bounds
            .Expand(0.09, minimumLongitudePadding: 8, minimumLatitudePadding: 6);

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#EAF1F8"));

        var frame = new SKRect(42, 42, width - 42, height - 42);
        bounds = bounds.FitAspectRatio(frame.Width / frame.Height);

        using var oceanPaint = new SKPaint { Color = SKColor.Parse("#EAF1F8"), Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRoundRect(frame, 20, 20, oceanPaint);

        using var inactiveFill = new SKPaint { Color = SKColor.Parse("#F7F9FC"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var inactiveLine = new SKPaint { Color = SKColor.Parse("#AAB5C3"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var activeLine = new SKPaint { Color = SKColor.Parse("#34465E"), Style = SKPaintStyle.Stroke, StrokeWidth = 2.1f, IsAntialias = true };

        var maximumUnits = Math.Max(1, countries.Count == 0 ? 1 : countries.Max(country => country.TotalUnits));

        canvas.Save();
        canvas.ClipRect(frame, SKClipOperation.Intersect, true);
        foreach (var feature in features)
        {
            if (!feature.Bounds.Intersects(bounds))
            {
                continue;
            }

            var active = activeByIso.TryGetValue(feature.Iso3, out var country);
            var fill = active
                ? new SKPaint
                {
                    Color = ActiveShade(country!.TotalUnits, maximumUnits),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                }
                : inactiveFill;

            foreach (var polygon in feature.Polygons)
            {
                using var path = BuildPath(polygon, bounds, frame);
                if (path.IsEmpty)
                {
                    continue;
                }

                canvas.DrawPath(path, fill);
                canvas.DrawPath(path, active ? activeLine : inactiveLine);
            }

            if (active)
            {
                fill.Dispose();
            }
        }
        canvas.Restore();

        DrawLabels(canvas, activeFeatures, activeByIso, bounds, frame);
        DrawLegend(canvas, countries, width, height, maximumUnits);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 96)
            ?? throw new InvalidOperationException("The FFC map image could not be encoded.");
        return encoded.ToArray();
    }

    private static void DrawLabels(
        SKCanvas canvas,
        IReadOnlyList<MapFeature> activeFeatures,
        IReadOnlyDictionary<string, FfcPresentationCountry> activeByIso,
        GeoBounds bounds,
        SKRect frame)
    {
        using var labelPaint = new SKPaint
        {
            Color = SKColor.Parse("#12223A"),
            TextSize = 22,
            Typeface = SKTypeface.Default,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            FakeBoldText = true
        };
        using var haloPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 235),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var haloBorder = new SKPaint
        {
            Color = SKColor.Parse("#CBD5E1"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.1f,
            IsAntialias = true
        };
        using var leaderPaint = new SKPaint
        {
            Color = SKColor.Parse("#64748B"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.35f,
            IsAntialias = true
        };

        var occupied = new List<SKRect>();
        foreach (var feature in activeFeatures
                     .OrderBy(feature => feature.Bounds.LongitudeSpan * feature.Bounds.LatitudeSpan)
                     .ThenByDescending(feature => activeByIso[feature.Iso3].TotalUnits)
                     .Take(18))
        {
            var anchor = Project(feature.LabelPoint, bounds, frame);
            var labelWidth = Math.Max(48, labelPaint.MeasureText(feature.Iso3) + 22);
            const float labelHeight = 34;
            var placement = FindBestLabelPlacement(
                feature.Iso3,
                anchor,
                labelWidth,
                labelHeight,
                frame,
                occupied);

            if (Math.Abs(placement.Center.X - anchor.X) > 8 || Math.Abs(placement.Center.Y - anchor.Y) > 8)
            {
                var lineEnd = NearestPointOnRect(placement.Rect, anchor);
                canvas.DrawLine(anchor.X, anchor.Y, lineEnd.X, lineEnd.Y, leaderPaint);
            }

            canvas.DrawRoundRect(placement.Rect, 8, 8, haloPaint);
            canvas.DrawRoundRect(placement.Rect, 8, 8, haloBorder);
            canvas.DrawText(feature.Iso3, placement.Center.X, placement.Center.Y + 7, labelPaint);
            occupied.Add(placement.Rect);
        }
    }

    private static LabelPlacement FindBestLabelPlacement(
        string isoCode,
        SKPoint anchor,
        float labelWidth,
        float labelHeight,
        SKRect frame,
        IReadOnlyList<SKRect> occupied)
    {
        LabelPlacement? best = null;
        var bestScore = double.MaxValue;

        foreach (var offset in LabelOffsets(isoCode))
        {
            var centre = new SKPoint(anchor.X + offset.X, anchor.Y + offset.Y);
            centre = ClampLabelCentre(centre, labelWidth, labelHeight, frame);
            var candidate = BuildLabelRect(centre, labelWidth, labelHeight);
            var overlap = occupied.Sum(existing => IntersectionArea(ExpandRect(existing, 7), candidate));
            var distance = Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y));
            var score = (overlap * 10_000d) + distance;

            if (overlap <= 0.01f)
            {
                return new LabelPlacement(centre, candidate);
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = new LabelPlacement(centre, candidate);
            }
        }

        return best ?? new LabelPlacement(
            ClampLabelCentre(anchor, labelWidth, labelHeight, frame),
            BuildLabelRect(ClampLabelCentre(anchor, labelWidth, labelHeight, frame), labelWidth, labelHeight));
    }

    private static IEnumerable<SKPoint> LabelOffsets(string isoCode)
    {
        if (PreferredLabelOffsets.TryGetValue(isoCode, out var preferred))
        {
            foreach (var offset in preferred)
            {
                yield return offset;
            }
        }

        foreach (var offset in GenericLabelOffsets)
        {
            yield return offset;
        }
    }

    private static SKPoint ClampLabelCentre(
        SKPoint centre,
        float width,
        float height,
        SKRect frame)
        => new(
            Math.Clamp(centre.X, frame.Left + (width / 2) + 3, frame.Right - (width / 2) - 3),
            Math.Clamp(centre.Y, frame.Top + (height / 2) + 3, frame.Bottom - (height / 2) - 3));

    private static SKPoint NearestPointOnRect(SKRect rect, SKPoint point)
        => new(
            Math.Clamp(point.X, rect.Left, rect.Right),
            Math.Clamp(point.Y, rect.Top, rect.Bottom));

    private static SKRect ExpandRect(SKRect rect, float margin)
        => new(rect.Left - margin, rect.Top - margin, rect.Right + margin, rect.Bottom + margin);

    private static float IntersectionArea(SKRect first, SKRect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return right <= left || bottom <= top ? 0 : (right - left) * (bottom - top);
    }

    private static SKRect BuildLabelRect(SKPoint center, float width, float height)
        => new(
            center.X - width / 2,
            center.Y - height / 2,
            center.X + width / 2,
            center.Y + height / 2);

    private static void DrawLegend(
        SKCanvas canvas,
        IReadOnlyList<FfcPresentationCountry> countries,
        int width,
        int height,
        int maximumUnits)
    {
        var legend = new SKRect(68, height - 252, 365, height - 68);
        using var background = new SKPaint { Color = new SKColor(255, 255, 255, 235), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var border = new SKPaint { Color = SKColor.Parse("#CBD5E1"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var title = new SKPaint { Color = SKColor.Parse("#12223A"), TextSize = 21, FakeBoldText = true, IsAntialias = true };
        using var text = new SKPaint { Color = SKColor.Parse("#526176"), TextSize = 17, IsAntialias = true };

        canvas.DrawRoundRect(legend, 14, 14, background);
        canvas.DrawRoundRect(legend, 14, 14, border);
        canvas.DrawText("Total quantity", legend.Left + 18, legend.Top + 31, title);

        var ranges = BuildLegendRanges(maximumUnits);
        var y = legend.Top + 58;
        foreach (var range in ranges)
        {
            using var sample = new SKPaint { Color = ActiveShade(range.End, maximumUnits), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(legend.Left + 18, y - 13, legend.Left + 53, y + 3), 4, 4, sample);
            canvas.DrawText(range.Label, legend.Left + 66, y + 1, text);
            y += 25;
        }

        var total = countries.Sum(country => country.TotalUnits);
        canvas.DrawText($"{countries.Count} countries · Total {total}", legend.Left + 18, legend.Bottom - 13, text);
    }

    private static IReadOnlyList<LegendRange> BuildLegendRanges(int maximum)
    {
        maximum = Math.Max(1, maximum);
        var bandCount = Math.Min(4, maximum);
        var ranges = new List<LegendRange>(bandCount);
        var start = 1;

        for (var index = 0; index < bandCount; index++)
        {
            var remainingValues = maximum - start + 1;
            var remainingBands = bandCount - index;
            var width = index == bandCount - 1
                ? remainingValues
                : Math.Max(1, remainingValues / remainingBands);
            var end = index == bandCount - 1
                ? maximum
                : Math.Min(maximum, start + width - 1);
            ranges.Add(new LegendRange(
                start,
                end,
                start == end
                    ? start.ToString(CultureInfo.InvariantCulture)
                    : $"{start.ToString(CultureInfo.InvariantCulture)}–{end.ToString(CultureInfo.InvariantCulture)}"));
            start = end + 1;
        }

        return ranges;
    }

    private static SKColor ActiveShade(int value, int maximum)
    {
        var ranges = BuildLegendRanges(maximum);
        var bandIndex = 0;
        for (var index = 0; index < ranges.Count; index++)
        {
            if (value >= ranges[index].Start && value <= ranges[index].End)
            {
                bandIndex = index;
                break;
            }
        }

        var paletteIndex = ranges.Count <= 1
            ? 0
            : (int)Math.Round(
                bandIndex * (ActiveBandColors.Length - 1d) / (ranges.Count - 1d),
                MidpointRounding.AwayFromZero);
        return ActiveBandColors[Math.Clamp(paletteIndex, 0, ActiveBandColors.Length - 1)];
    }

    private static SKPath BuildPath(
        IReadOnlyList<IReadOnlyList<GeoPoint>> polygon,
        GeoBounds bounds,
        SKRect frame)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var ring in polygon)
        {
            if (ring.Count < 3)
            {
                continue;
            }

            var first = Project(ring[0], bounds, frame);
            path.MoveTo(first);
            for (var index = 1; index < ring.Count; index++)
            {
                path.LineTo(Project(ring[index], bounds, frame));
            }
            path.Close();
        }
        return path;
    }

    private static SKPoint Project(GeoPoint point, GeoBounds bounds, SKRect frame)
    {
        var x = frame.Left + (float)((point.Longitude - bounds.MinimumLongitude) / bounds.LongitudeSpan * frame.Width);
        var y = frame.Top + (float)((bounds.MaximumLatitude - point.Latitude) / bounds.LatitudeSpan * frame.Height);
        return new SKPoint(x, y);
    }

    private static IReadOnlyList<MapFeature> LoadFeatures(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The simplified world map required for FFC PowerPoint export was not found.",
                path);
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var result = new List<MapFeature>();

        foreach (var featureElement in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var properties = featureElement.GetProperty("properties");
            var iso3 = ReadProperty(properties, "iso3", "ISO_A3", "ADM0_A3").ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(iso3))
            {
                continue;
            }

            var name = ReadProperty(properties, "name", "NAME", "ADMIN");
            var geometry = featureElement.GetProperty("geometry");
            var type = geometry.GetProperty("type").GetString();
            var coordinates = geometry.GetProperty("coordinates");
            var polygons = new List<IReadOnlyList<IReadOnlyList<GeoPoint>>>();

            if (string.Equals(type, "Polygon", StringComparison.OrdinalIgnoreCase))
            {
                polygons.Add(ReadPolygon(coordinates));
            }
            else if (string.Equals(type, "MultiPolygon", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var polygon in coordinates.EnumerateArray())
                {
                    polygons.Add(ReadPolygon(polygon));
                }
            }

            var primaryPolygon = polygons
                .Select(polygon => new
                {
                    Polygon = polygon,
                    Points = polygon.SelectMany(ring => ring).ToArray()
                })
                .Where(item => item.Points.Length > 0)
                .Select(item => new
                {
                    item.Polygon,
                    item.Points,
                    Bounds = GeoBounds.From(item.Points)
                })
                .OrderByDescending(item => item.Bounds.LongitudeSpan * item.Bounds.LatitudeSpan)
                .FirstOrDefault();

            if (primaryPolygon is null)
            {
                continue;
            }

            var featureBounds = primaryPolygon.Bounds;
            result.Add(new MapFeature(
                iso3,
                name,
                polygons,
                featureBounds,
                new GeoPoint(
                    (featureBounds.MinimumLongitude + featureBounds.MaximumLongitude) / 2,
                    (featureBounds.MinimumLatitude + featureBounds.MaximumLatitude) / 2),
                primaryPolygon.Points));
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<GeoPoint>> ReadPolygon(JsonElement polygon)
    {
        var rings = new List<IReadOnlyList<GeoPoint>>();
        foreach (var ringElement in polygon.EnumerateArray())
        {
            var ring = ringElement
                .EnumerateArray()
                .Where(point => point.GetArrayLength() >= 2)
                .Select(point => new GeoPoint(point[0].GetDouble(), point[1].GetDouble()))
                .ToArray();
            if (ring.Length >= 3)
            {
                rings.Add(ring);
            }
        }
        return rings;
    }

    private static string ReadProperty(JsonElement properties, params string[] names)
    {
        foreach (var name in names)
        {
            if (properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private sealed record MapFeature(
        string Iso3,
        string Name,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>> Polygons,
        GeoBounds Bounds,
        GeoPoint LabelPoint,
        IReadOnlyList<GeoPoint> PrimaryPoints)
    {
        public IEnumerable<GeoPoint> AllPoints => PrimaryPoints;
    }

    private readonly record struct GeoPoint(double Longitude, double Latitude);

    private readonly record struct GeoBounds(
        double MinimumLongitude,
        double MinimumLatitude,
        double MaximumLongitude,
        double MaximumLatitude)
    {
        public double LongitudeSpan => Math.Max(1, MaximumLongitude - MinimumLongitude);
        public double LatitudeSpan => Math.Max(1, MaximumLatitude - MinimumLatitude);

        public static GeoBounds From(IEnumerable<GeoPoint> points)
        {
            var array = points.ToArray();
            if (array.Length == 0)
            {
                return new GeoBounds(-25, -60, 120, 60);
            }
            return new GeoBounds(
                array.Min(point => point.Longitude),
                array.Min(point => point.Latitude),
                array.Max(point => point.Longitude),
                array.Max(point => point.Latitude));
        }

        public GeoBounds Expand(
            double factor,
            double minimumLongitudePadding,
            double minimumLatitudePadding)
        {
            var longitudePadding = Math.Max(minimumLongitudePadding, LongitudeSpan * factor);
            var latitudePadding = Math.Max(minimumLatitudePadding, LatitudeSpan * factor);
            return new GeoBounds(
                Math.Max(-180, MinimumLongitude - longitudePadding),
                Math.Max(-85, MinimumLatitude - latitudePadding),
                Math.Min(180, MaximumLongitude + longitudePadding),
                Math.Min(85, MaximumLatitude + latitudePadding));
        }

        public GeoBounds FitAspectRatio(double targetRatio)
        {
            targetRatio = Math.Max(.25, targetRatio);
            var currentRatio = LongitudeSpan / LatitudeSpan;
            if (Math.Abs(currentRatio - targetRatio) < .001)
            {
                return this;
            }

            var centreLongitude = (MinimumLongitude + MaximumLongitude) / 2;
            var centreLatitude = (MinimumLatitude + MaximumLatitude) / 2;
            var longitudeSpan = LongitudeSpan;
            var latitudeSpan = LatitudeSpan;

            if (currentRatio < targetRatio)
            {
                longitudeSpan = latitudeSpan * targetRatio;
            }
            else
            {
                latitudeSpan = longitudeSpan / targetRatio;
            }

            var halfLongitude = longitudeSpan / 2;
            var halfLatitude = latitudeSpan / 2;
            return new GeoBounds(
                Math.Max(-180, centreLongitude - halfLongitude),
                Math.Max(-85, centreLatitude - halfLatitude),
                Math.Min(180, centreLongitude + halfLongitude),
                Math.Min(85, centreLatitude + halfLatitude));
        }

        public bool Intersects(GeoBounds other)
            => !(MaximumLongitude < other.MinimumLongitude ||
                 MinimumLongitude > other.MaximumLongitude ||
                 MaximumLatitude < other.MinimumLatitude ||
                 MinimumLatitude > other.MaximumLatitude);
    }

    private readonly record struct LabelPlacement(SKPoint Center, SKRect Rect);

    private sealed record LegendRange(int Start, int End, string Label);
}
