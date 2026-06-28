using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceCandidateSearchServiceTests
{
    [Fact]
    public void CosineSimilarity_ReturnsOneForEquivalentDirection()
    {
        var first = new[] { 1f, 2f, 3f };
        var second = new[] { 2f, 4f, 6f };

        var similarity = FaceCandidateSearchService.CosineSimilarity(first, second);

        Assert.Equal(1d, similarity, 6);
    }

    [Fact]
    public void CosineSimilarity_ReturnsZeroForOrthogonalVectors()
    {
        var similarity = FaceCandidateSearchService.CosineSimilarity(
            new[] { 1f, 0f },
            new[] { 0f, 1f });

        Assert.Equal(0d, similarity, 6);
    }

    [Fact]
    public void CosineSimilarity_RejectsInvalidVectors()
    {
        var invalidPairs = new[]
        {
            (First: Array.Empty<float>(), Second: Array.Empty<float>()),
            (First: new[] { 1f }, Second: new[] { 1f, 2f }),
            (First: new[] { 0f, 0f }, Second: new[] { 1f, 0f })
        };

        foreach (var pair in invalidPairs)
        {
            Assert.Equal(-1d, FaceCandidateSearchService.CosineSimilarity(pair.First, pair.Second));
        }
    }

}
