using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceSimilarityScoringTests
{
    [Fact]
    public void ScoreReferences_UsesRepeatableEvidenceAcrossReferences()
    {
        var query = new[] { 1f, 0f, 0f };
        IReadOnlyList<float>[] references =
        {
            new[] { 1f, 0f, 0f },
            new[] { .98f, .2f, 0f },
            new[] { .96f, .28f, 0f }
        };

        var score = FaceSimilarityScoring.ScoreReferences(query, references, 8);

        Assert.Equal(3, score.ReferenceCount);
        Assert.True(score.BestSimilarity > .99d);
        Assert.True(score.MeanTopSimilarity > .96d);
        Assert.True(score.AggregateSimilarity > .97d);
    }

    [Fact]
    public void ScoreReferences_DoesNotLetOneCloseReferenceHidePoorRepeatability()
    {
        var query = new[] { 1f, 0f, 0f };
        IReadOnlyList<float>[] references =
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f },
            new[] { 0f, 0f, 1f }
        };

        var score = FaceSimilarityScoring.ScoreReferences(query, references, 8);

        Assert.Equal(1d, score.BestSimilarity, 6);
        Assert.True(score.AggregateSimilarity < score.BestSimilarity);
        Assert.True(score.AggregateSimilarity < .8d);
    }

    [Fact]
    public void CreateNormalisedCentroid_ReturnsUnitVector()
    {
        IReadOnlyList<float>[] vectors =
        {
            new[] { 1f, 0f },
            new[] { 0.8f, 0.6f }
        };

        var centroid = FaceSimilarityScoring.CreateNormalisedCentroid(vectors);
        var norm = Math.Sqrt(centroid.Sum(value => value * value));

        Assert.Equal(2, centroid.Length);
        Assert.Equal(1d, norm, 6);
        Assert.True(FaceSimilarityScoring.CosineSimilarity(centroid, vectors[0]) > .9d);
    }

    [Fact]
    public void CreateNormalisedCentroid_RejectsMixedDimensions()
    {
        IReadOnlyList<float>[] vectors =
        {
            new[] { 1f, 0f },
            new[] { 1f, 0f, 0f }
        };

        Assert.Throws<ArgumentException>(() =>
            FaceSimilarityScoring.CreateNormalisedCentroid(vectors));
    }
}
