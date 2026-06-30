using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceCandidateEvidencePolicyTests
{
    private static readonly MediaPeopleOptions Options = new()
    {
        CandidateSimilarityThreshold = 0.58,
        CandidateSingleReferenceSimilarityThreshold = 0.70,
        CandidateStrongSimilarityThreshold = 0.72,
        CandidateStrongMeanSimilarityThreshold = 0.68,
        CandidateMinimumMargin = 0.08,
        CandidateMinimumTrustedReferencesForStrong = 2
    };

    [Fact]
    public void Single_reference_below_open_set_threshold_is_not_suggested()
    {
        var evidence = FaceCandidateEvidencePolicy.Evaluate(
            similarity: 0.534,
            bestReferenceSimilarity: 0.534,
            meanTopSimilarity: 0.534,
            referenceCount: 1,
            nextPersonSimilarity: null,
            Options);

        Assert.False(evidence.ShouldSuggest);
        Assert.Equal(FaceCandidateConfidenceLevel.None, evidence.ConfidenceLevel);
        Assert.False(evidence.MarginAvailable);
        Assert.Null(evidence.MarginToNext);
    }

    [Fact]
    public void Single_reference_high_similarity_is_possible_but_never_strong()
    {
        var evidence = FaceCandidateEvidencePolicy.Evaluate(
            similarity: 0.789,
            bestReferenceSimilarity: 0.789,
            meanTopSimilarity: 0.789,
            referenceCount: 1,
            nextPersonSimilarity: null,
            Options);

        Assert.True(evidence.ShouldSuggest);
        Assert.Equal(FaceCandidateConfidenceLevel.Possible, evidence.ConfidenceLevel);
        Assert.False(evidence.MarginAvailable);
        Assert.Null(evidence.MarginToNext);
    }

    [Fact]
    public void Multiple_references_without_a_runner_up_remain_possible()
    {
        var evidence = FaceCandidateEvidencePolicy.Evaluate(
            similarity: 0.82,
            bestReferenceSimilarity: 0.84,
            meanTopSimilarity: 0.79,
            referenceCount: 3,
            nextPersonSimilarity: null,
            Options);

        Assert.Equal(FaceCandidateConfidenceLevel.Possible, evidence.ConfidenceLevel);
        Assert.False(evidence.MarginAvailable);
        Assert.Null(evidence.MarginToNext);
    }

    [Fact]
    public void Strong_requires_multiple_references_and_real_separation()
    {
        var evidence = FaceCandidateEvidencePolicy.Evaluate(
            similarity: 0.80,
            bestReferenceSimilarity: 0.84,
            meanTopSimilarity: 0.74,
            referenceCount: 3,
            nextPersonSimilarity: 0.64,
            Options);

        Assert.Equal(FaceCandidateConfidenceLevel.Strong, evidence.ConfidenceLevel);
        Assert.True(evidence.MarginAvailable);
        Assert.Equal(0.16d, evidence.MarginToNext!.Value, 6);
    }

    [Fact]
    public void Inadequate_separation_prevents_strong_label()
    {
        var evidence = FaceCandidateEvidencePolicy.Evaluate(
            similarity: 0.80,
            bestReferenceSimilarity: 0.84,
            meanTopSimilarity: 0.74,
            referenceCount: 3,
            nextPersonSimilarity: 0.76,
            Options);

        Assert.Equal(FaceCandidateConfidenceLevel.Possible, evidence.ConfidenceLevel);
        Assert.True(evidence.MarginAvailable);
        Assert.Equal(0.04d, evidence.MarginToNext!.Value, 6);
    }
}
