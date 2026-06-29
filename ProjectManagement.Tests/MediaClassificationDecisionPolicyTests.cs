using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class MediaClassificationDecisionPolicyTests
{
    [Fact]
    public void Decide_RequiresClearSeparationFromRunnerUp()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Screenshot, .70),
            (MediaClassification.Diagram, .65));

        var result = policy.Decide(
            MediaClassification.Screenshot,
            .70,
            scores,
            Array.Empty<string>());

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal(MediaClassification.Unknown, result.EffectiveClassification);
        Assert.Equal("AMBIGUOUS_SCORE_MARGIN", result.ReasonCode);
    }

    [Fact]
    public void Decide_UsesStricterPhotographThreshold()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Photograph, .86),
            (MediaClassification.Graphic, .05));

        var result = policy.Decide(
            MediaClassification.Photograph,
            .86,
            scores,
            Array.Empty<string>());

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal("BELOW_CATEGORY_THRESHOLD", result.ReasonCode);
    }

    [Fact]
    public void Decide_BlocksPhotographWhenNonPhotoProbabilityRemainsMaterial()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Photograph, .89),
            (MediaClassification.Screenshot, .10),
            (MediaClassification.Unknown, .01));

        var result = policy.Decide(
            MediaClassification.Photograph,
            .89,
            scores,
            Array.Empty<string>());

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal("CONFLICTING_NON_PHOTO_EVIDENCE", result.ReasonCode);
    }

    [Fact]
    public void Decide_AcceptsClearHighConfidencePrediction()
    {
        var policy = CreatePolicy();
        var scores = Scores(
            (MediaClassification.Diagram, .91),
            (MediaClassification.Graphic, .04));

        var result = policy.Decide(
            MediaClassification.Diagram,
            .91,
            scores,
            Array.Empty<string>());

        Assert.Equal(MediaClassificationDecisionStatus.AutomaticallyAccepted, result.Status);
        Assert.Equal(MediaClassification.Diagram, result.EffectiveClassification);
        Assert.Equal("AUTO_ACCEPTED_HIGH_CONFIDENCE", result.ReasonCode);
    }

    [Fact]
    public void Decide_DoesNotAcceptDisabledCategory()
    {
        var options = new MediaLibraryOptions
        {
            Classification = new MediaClassificationOptions
            {
                DiagramDetectionEnabled = false
            }
        };
        var policy = new MediaClassificationDecisionPolicy(Options.Create(options));
        var scores = Scores((MediaClassification.Diagram, .99));

        var result = policy.Decide(
            MediaClassification.Diagram,
            .99,
            scores,
            Array.Empty<string>());

        Assert.Equal(MediaClassificationDecisionStatus.NeedsReview, result.Status);
        Assert.Equal("CATEGORY_DISABLED", result.ReasonCode);
    }

    private static MediaClassificationDecisionPolicy CreatePolicy()
        => new(Options.Create(new MediaLibraryOptions()));

    private static IReadOnlyDictionary<MediaClassification, double> Scores(
        params (MediaClassification Classification, double Score)[] values)
    {
        var scores = Enum.GetValues<MediaClassification>()
            .ToDictionary(classification => classification, _ => 0d);
        foreach (var (classification, score) in values)
        {
            scores[classification] = score;
        }

        return scores;
    }
}
