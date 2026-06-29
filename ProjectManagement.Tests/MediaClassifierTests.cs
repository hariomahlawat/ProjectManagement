using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class MediaClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_RecognisesStrongScreenshotFilename()
    {
        var path = await CreateImageAsync("screenshot", 640, 360, new Rgba32(240, 240, 240));
        try
        {
            var result = await CreateClassifier().ClassifyAsync(path, Metadata(path, "image/png", 640, 360), CancellationToken.None);
            Assert.Equal(MediaClassification.Screenshot, result.PredictedClassification);
            Assert.Equal(MediaClassificationDecisionStatus.AutomaticallyAccepted, result.DecisionStatus);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClassifyAsync_UsesFaceEvidenceForAmbiguousPortrait()
    {
        var path = await CreateImageAsync("portrait", 480, 640, new Rgba32(160, 130, 110));
        try
        {
            var classifier = CreateClassifier(new StubFacePresenceProbe(true));
            var result = await classifier.ClassifyAsync(path, Metadata(path, "image/jpeg", 480, 640), CancellationToken.None);
            Assert.Equal(MediaClassification.Photograph, result.PredictedClassification);
            Assert.Equal(MediaClassification.Photograph, result.EffectiveClassification);
            Assert.Contains(result.Signals, x => x.Contains("face", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClassifyAsync_StrongDrawIoFilenameIsDiagram()
    {
        var path = await CreateImageAsync("diagram", 1000, 600, new Rgba32(255, 255, 255));
        try
        {
            var classifier = CreateClassifier(new StubFacePresenceProbe(false));
            var content = new MediaContentDescriptor("SDD_Proliferation_Flow_Chart.drawio.png", "image/png", new FileInfo(path).Length,
                DateTimeOffset.UtcNow, _ => Task.FromResult<Stream>(File.OpenRead(path)));
            var result = await classifier.ClassifyAsync(content, Metadata(path, "image/png", 1000, 600), CancellationToken.None);
            Assert.Equal(MediaClassification.Diagram, result.PredictedClassification);
            Assert.NotEqual(MediaClassification.Photograph, result.EffectiveClassification);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsNotApplicableWhenDisabled()
    {
        var options = new MediaLibraryOptions { Classification = new MediaClassificationOptions { Enabled = false } };
        var classifier = new MediaClassifier(Options.Create(options), new StubFacePresenceProbe(false),
            new MediaClassificationDecisionPolicy(Options.Create(options)));
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var content = new MediaContentDescriptor("x.png", "image/png", 3, DateTimeOffset.UtcNow, _ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));
        var result = await classifier.ClassifyAsync(content, Metadata(null, "image/png", 1, 1), CancellationToken.None);
        Assert.Equal(MediaClassificationDecisionStatus.NotApplicable, result.DecisionStatus);
    }

    private static MediaClassifier CreateClassifier(IFacePresenceProbe? probe = null)
    {
        var options = Options.Create(new MediaLibraryOptions());
        return new MediaClassifier(options, probe ?? new StubFacePresenceProbe(false), new MediaClassificationDecisionPolicy(options));
    }

    private static MediaFileMetadata Metadata(string? path, string type, int width, int height)
        => new(MediaAssetKind.Photo, type, path is null ? 0 : new FileInfo(path).Length, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, width, height, null, false, null, null);

    private static async Task<string> CreateImageAsync(string prefix, int width, int height, Rgba32 colour)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height, colour);
        await image.SaveAsPngAsync(path);
        return path;
    }

    private sealed class StubFacePresenceProbe(bool detected) : IFacePresenceProbe
    {
        public Task<FacePresenceResult> AnalyseAsync(byte[] imageBytes, CancellationToken cancellationToken)
            => Task.FromResult(detected
                ? new FacePresenceResult(true, true, 1, .96, 160, 160, .08, true)
                : new FacePresenceResult(true, false, 0, 0, 0, 0, 0, false));
    }
}
