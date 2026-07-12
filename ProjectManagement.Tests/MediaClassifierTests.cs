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
    public async Task ClassifyAsync_UsesFaceEvidenceOnlyForNaturalPhotoBaseline()
    {
        var path = await CreateTexturedPortraitImageAsync(480, 640);
        try
        {
            var classifier = CreateClassifier(new StubFacePresenceProbe(true));
            var result = await classifier.ClassifyAsync(
                Content(path, "portrait-at-event.jpg", "image/jpeg"),
                MetadataFromImage(path, "image/jpeg"),
                CancellationToken.None);

            Assert.Equal(MediaClassification.Photograph, result.PredictedClassification);
            Assert.True(result.Safety.NaturalPhotoBaselineSatisfied);
            Assert.False(result.Safety.HasStructuralVeto);
            Assert.True(result.Safety.FaceEvidenceUsed);
            Assert.Equal("FACE_EVIDENCE_USED_AS_SUPPORT", result.Safety.FaceEvidenceDecisionCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ClassifyAsync_CartoonWorksheetCannotBeAdmittedByFaceDetection()
    {
        var path = Fixture("worksheet-with-cartoon-faces.png");
        var classifier = CreateClassifier(new StubFacePresenceProbe(true));
        var result = await classifier.ClassifyAsync(
            Content(path, "all-about-me.png", "image/png"),
            MetadataFromImage(path, "image/png"),
            CancellationToken.None);

        Assert.NotEqual(MediaClassification.Photograph, result.EffectiveClassification);
        Assert.False(result.Safety.FaceEvidenceUsed);
        Assert.True(
            result.Safety.HasStructuralVeto || !result.Safety.NaturalPhotoBaselineSatisfied,
            "The worksheet must fail the natural-photograph safety gate.");
        Assert.NotEqual(MediaClassificationDecisionStatus.AutomaticallyAccepted, result.DecisionStatus);
    }

    [Fact]
    public async Task ClassifyAsync_StrongDrawIoFilenameIsDiagram()
    {
        var path = Fixture("flow-chart.drawio.png");
        var classifier = CreateClassifier(new StubFacePresenceProbe(false));
        var result = await classifier.ClassifyAsync(
            Content(path, "SDD_Proliferation_Flow_Chart.drawio.png", "image/png"),
            MetadataFromImage(path, "image/png"),
            CancellationToken.None);

        Assert.Equal(MediaClassification.Diagram, result.PredictedClassification);
        Assert.NotEqual(MediaClassification.Photograph, result.EffectiveClassification);
    }

    [Fact]
    public async Task ClassifyAsync_OutdoorDroneImageRemainsAPhotographCandidateWithoutFaceEvidence()
    {
        var path = Fixture("outdoor-drone-photograph.jpg");
        var classifier = CreateClassifier(new StubFacePresenceProbe(false));
        var result = await classifier.ClassifyAsync(
            Content(path, "swarm-drones.jpg", "image/jpeg"),
            MetadataFromImage(path, "image/jpeg"),
            CancellationToken.None);

        Assert.Equal(MediaClassification.Photograph, result.PredictedClassification);
        Assert.False(result.Safety.FaceEvidenceUsed);
        Assert.NotEqual(MediaClassification.ScannedDocument, result.PredictedClassification);
    }

    [Fact]
    public async Task ClassifyAsync_HonoursDisabledDiagramDetector()
    {
        var path = await CreateImageAsync("diagram", 1000, 600, new Rgba32(255, 255, 255));
        try
        {
            var optionsValue = new MediaLibraryOptions
            {
                Classification = new MediaClassificationOptions
                {
                    DiagramDetectionEnabled = false
                }
            };
            var options = Options.Create(optionsValue);
            var classifier = new MediaClassifier(
                options,
                new StubFacePresenceProbe(false),
                new MediaClassificationDecisionPolicy(options));
            var content = new MediaContentDescriptor(
                "architecture-flowchart.drawio.png",
                "image/png",
                new FileInfo(path).Length,
                DateTimeOffset.UtcNow,
                _ => Task.FromResult<Stream>(File.OpenRead(path)));

            var result = await classifier.ClassifyAsync(
                content,
                Metadata(path, "image/png", 1000, 600),
                CancellationToken.None);

            Assert.NotEqual(MediaClassification.Diagram, result.PredictedClassification);
            Assert.NotEqual(MediaClassification.Diagram, result.EffectiveClassification);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClassifyAsync_DoesNotMatchDocumentKeywordInsideUniform()
    {
        var path = await CreateImageAsync("uniform", 480, 640, new Rgba32(110, 125, 105));
        try
        {
            var classifier = CreateClassifier(new StubFacePresenceProbe(false));
            var content = new MediaContentDescriptor(
                "uniform-portrait.jpg",
                "image/jpeg",
                new FileInfo(path).Length,
                DateTimeOffset.UtcNow,
                _ => Task.FromResult<Stream>(File.OpenRead(path)));

            var result = await classifier.ClassifyAsync(
                content,
                Metadata(path, "image/jpeg", 480, 640),
                CancellationToken.None);

            Assert.NotEqual(MediaClassification.ScannedDocument, result.PredictedClassification);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsNotApplicableWhenDisabled()
    {
        var options = new MediaLibraryOptions { Classification = new MediaClassificationOptions { Enabled = false } };
        var classifier = new MediaClassifier(
            Options.Create(options),
            new StubFacePresenceProbe(false),
            new MediaClassificationDecisionPolicy(Options.Create(options)));
        var content = new MediaContentDescriptor(
            "x.png",
            "image/png",
            3,
            DateTimeOffset.UtcNow,
            _ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));

        var result = await classifier.ClassifyAsync(
            content,
            Metadata(null, "image/png", 1, 1),
            CancellationToken.None);

        Assert.Equal(MediaClassificationDecisionStatus.NotApplicable, result.DecisionStatus);
    }

    private static MediaClassifier CreateClassifier(IFacePresenceProbe? probe = null)
    {
        var options = Options.Create(new MediaLibraryOptions());
        return new MediaClassifier(
            options,
            probe ?? new StubFacePresenceProbe(false),
            new MediaClassificationDecisionPolicy(options));
    }

    private static MediaContentDescriptor Content(string path, string fileName, string contentType)
        => new(
            fileName,
            contentType,
            new FileInfo(path).Length,
            File.GetLastWriteTimeUtc(path),
            _ => Task.FromResult<Stream>(File.OpenRead(path)));

    private static MediaFileMetadata MetadataFromImage(string path, string type)
    {
        var dimensions = Image.Identify(path)
                         ?? throw new InvalidOperationException($"Unable to identify test image '{path}'.");
        return Metadata(path, type, dimensions.Width, dimensions.Height);
    }

    private static MediaFileMetadata Metadata(string? path, string type, int width, int height)
        => new(
            MediaAssetKind.Photo,
            type,
            path is null ? 0 : new FileInfo(path).Length,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            width,
            height,
            null,
            false,
            null,
            null);

    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "TestData", "MediaClassification", name);

    private static async Task<string> CreateImageAsync(string prefix, int width, int height, Rgba32 colour)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.png");
        using var image = new Image<Rgba32>(width, height, colour);
        await image.SaveAsPngAsync(path);
        return path;
    }

    private static async Task<string> CreateTexturedPortraitImageAsync(int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"portrait-{Guid.NewGuid():N}.jpg");
        using var image = new Image<Rgba32>(width, height);
        var random = new Random(4319);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var shade = Math.Clamp(95 + (int)(75d * y / height) + random.Next(-18, 19), 0, 255);
                image[x, y] = new Rgba32(
                    (byte)Math.Clamp(shade + 22, 0, 255),
                    (byte)Math.Clamp(shade + 4, 0, 255),
                    (byte)Math.Clamp(shade - 10, 0, 255));
            }
        }

        await image.SaveAsJpegAsync(path);
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
