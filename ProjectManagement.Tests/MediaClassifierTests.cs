using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

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
