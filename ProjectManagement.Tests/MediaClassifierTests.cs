using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;
using SixLabors.ImageSharp;

namespace ProjectManagement.Tests;

public sealed class MediaClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_RecognisesTypicalScreenshot()
    {
        var classifier = CreateClassifier();
        var metadata = new MediaFileMetadata(
            MediaAssetKind.Photo,
            "image/png",
            1000,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1920,
            1080,
            null,
            false,
            null,
            null);

        var result = await classifier.ClassifyAsync(
            "Screenshot 2026-06-27.png",
            metadata,
            CancellationToken.None);

        Assert.Equal(MediaClassification.Screenshot, result.Classification);
        Assert.True(result.Confidence >= 0.62);
    }

    [Fact]
    public async Task ClassifyAsync_PrefersPhotographWhenCameraMetadataExists()
    {
        var classifier = CreateClassifier();
        var metadata = new MediaFileMetadata(
            MediaAssetKind.Photo,
            "image/jpeg",
            1000,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            4032,
            3024,
            null,
            true,
            "Canon",
            "EOS");

        var result = await classifier.ClassifyAsync("IMG_0001.jpg", metadata, CancellationToken.None);

        Assert.Equal(MediaClassification.Photograph, result.Classification);
        Assert.True(result.Confidence >= 0.90);
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsUnknownWhenClassificationIsDisabled()
    {
        var options = new MediaLibraryOptions
        {
            Classification = new MediaClassificationOptions
            {
                Enabled = false,
                ScreenshotDetectionEnabled = false
            }
        };
        var classifier = new MediaClassifier(Options.Create(options));
        var metadata = new MediaFileMetadata(
            MediaAssetKind.Photo,
            "image/png",
            1000,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            1920,
            1080,
            null,
            false,
            null,
            null);

        var result = await classifier.ClassifyAsync("Screenshot.png", metadata, CancellationToken.None);

        Assert.Equal(MediaClassification.Unknown, result.Classification);
    }

    private static MediaClassifier CreateClassifier()
        => new(Options.Create(new MediaLibraryOptions()));
}

public sealed class MediaClassifierNaturalImageTests
{
    [Fact]
    public async Task ClassifyAsync_RecognisesNaturalPortraitWithoutExif()
    {
        var path = Path.Combine(Path.GetTempPath(), $"portrait-{Guid.NewGuid():N}.jpg");
        try
        {
            using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(480, 640))
            {
                var random = new Random(42);
                image.ProcessPixelRows(accessor =>
                {
                    for (var y = 0; y < image.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (var x = 0; x < image.Width; x++)
                        {
                            var radial = Math.Sqrt(Math.Pow((x - 240d) / 240d, 2) + Math.Pow((y - 300d) / 340d, 2));
                            var noise = random.Next(-18, 19);
                            var r = Math.Clamp((int)(155 + (45 * Math.Sin(x / 37d)) - (30 * radial) + noise), 0, 255);
                            var g = Math.Clamp((int)(120 + (55 * Math.Sin(y / 43d)) - (20 * radial) + noise), 0, 255);
                            var b = Math.Clamp((int)(95 + (40 * Math.Cos((x + y) / 51d)) - (15 * radial) + noise), 0, 255);
                            row[x] = new SixLabors.ImageSharp.PixelFormats.Rgba32((byte)r, (byte)g, (byte)b);
                        }
                    }
                });
                await image.SaveAsJpegAsync(path);
            }

            var metadata = new MediaFileMetadata(
                MediaAssetKind.Photo,
                "image/jpeg",
                new FileInfo(path).Length,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                480,
                640,
                null,
                false,
                null,
                null);

            var classifier = new MediaClassifier(Options.Create(new MediaLibraryOptions()));
            var result = await classifier.ClassifyAsync(path, metadata, CancellationToken.None);

            Assert.Equal(MediaClassification.Photograph, result.Classification);
            Assert.True(result.Confidence >= 0.75, $"Confidence was {result.Confidence:P0}.");
            Assert.Contains(result.Signals, signal => signal.Contains("natural", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task ClassifyAsync_KeepsNamedFlowChartOutOfPhotographs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"flow-chart-{Guid.NewGuid():N}.png");
        try
        {
            using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1000, 600, new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 255, 255)))
            {
                image.ProcessPixelRows(accessor =>
                {
                    for (var y = 80; y < 520; y += 110)
                    {
                        for (var x = 80; x < 920; x++)
                        {
                            accessor.GetRowSpan(y)[x] = new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 255);
                            accessor.GetRowSpan(Math.Min(y + 60, 599))[x] = new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 255);
                        }
                    }

                    for (var x = 80; x < 920; x += 210)
                    {
                        for (var y = 80; y < 580; y++)
                            accessor.GetRowSpan(y)[x] = new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 255);
                    }
                });
                await image.SaveAsPngAsync(path);
            }

            var metadata = new MediaFileMetadata(
                MediaAssetKind.Photo,
                "image/png",
                new FileInfo(path).Length,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                1000,
                600,
                null,
                false,
                null,
                null);

            var classifier = new MediaClassifier(Options.Create(new MediaLibraryOptions()));
            var result = await classifier.ClassifyAsync("SDD_Proliferation_Flow_Chart.drawio.png", metadata, CancellationToken.None);

            Assert.NotEqual(MediaClassification.Photograph, result.Classification);
            Assert.True(result.Classification is MediaClassification.Diagram or MediaClassification.ScannedDocument or MediaClassification.PresentationSlide);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
