using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceGeometryTests
{
    [Fact]
    public void IntersectionOverUnion_ReturnsExpectedOverlap()
    {
        var first = new FaceRectangle(0, 0, 100, 100);
        var second = new FaceRectangle(50, 50, 100, 100);

        var result = FaceGeometry.IntersectionOverUnion(first, second);

        Assert.Equal(2500d / 17500d, result, 6);
    }

    [Fact]
    public void NonMaximumSuppression_KeepsHighestScoringOverlappingFace()
    {
        var candidates = new[]
        {
            new Candidate("primary", new FaceRectangle(10, 10, 80, 80), .95),
            new Candidate("duplicate", new FaceRectangle(13, 12, 80, 80), .91),
            new Candidate("separate", new FaceRectangle(200, 200, 60, 60), .88)
        };

        var selected = FaceGeometry.NonMaximumSuppression(
            candidates,
            candidate => candidate.Rectangle,
            candidate => candidate.Score,
            threshold: .30,
            maximumResults: 10);

        Assert.Equal(new[] { "primary", "separate" }, selected.Select(candidate => candidate.Name));
    }

    [Fact]
    public void TryCreateSimilarityTransform_MapsKnownRotationScaleAndTranslation()
    {
        var source = new[]
        {
            new FacePoint(0, 0),
            new FacePoint(10, 0),
            new FacePoint(0, 10)
        };
        var expected = source
            .Select(point => new FacePoint(
                2 * point.X - .5 * point.Y + 7,
                .5 * point.X + 2 * point.Y - 3))
            .ToArray();

        var created = FaceGeometry.TryCreateSimilarityTransform(source, expected, out var transform);

        Assert.True(created);
        for (var index = 0; index < source.Length; index++)
        {
            var mapped = transform.Map(source[index]);
            Assert.Equal(expected[index].X, mapped.X, 6);
            Assert.Equal(expected[index].Y, mapped.Y, 6);
        }
    }

    [Fact]
    public void TryCreateSimilarityTransform_RejectsDegenerateLandmarks()
    {
        var source = Enumerable.Repeat(new FacePoint(5, 5), 5).ToArray();
        var destination = Enumerable.Repeat(new FacePoint(10, 10), 5).ToArray();

        Assert.False(FaceGeometry.TryCreateSimilarityTransform(source, destination, out _));
    }

    private sealed record Candidate(string Name, FaceRectangle Rectangle, double Score);
}
