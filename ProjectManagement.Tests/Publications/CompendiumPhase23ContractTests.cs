using ProjectManagement.Models;
using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase23ContractTests
{
    [Fact]
    public void ImagePolicy_ClassifiesReferenceQualityByEffectiveDpi()
    {
        Assert.Equal(CompendiumImageQuality.Good, CompendiumPublicationImagePolicy.Classify(180));
        Assert.Equal(CompendiumImageQuality.Acceptable, CompendiumPublicationImagePolicy.Classify(150));
        Assert.Equal(CompendiumImageQuality.Low, CompendiumPublicationImagePolicy.Classify(149));
    }

    [Fact]
    public void ReviewFingerprint_ChangesWhenPublicationCropChanges()
    {
        var input = new CompendiumReviewFingerprintInput(
            42,
            "ASTRAE",
            ProjectLifecycleStatus.Active,
            "R&D",
            "AI",
            "Infantry",
            null,
            null,
            null,
            "Project description",
            7,
            CompendiumImageSelectionMode.Explicit,
            .5,
            .5);

        var first = CompendiumReviewFingerprint.Create(input);
        var changed = CompendiumReviewFingerprint.Create(input with { FocalX = .7 });

        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void ReadinessPolicy_MarksMatchingFingerprintReviewed()
    {
        const string fingerprint = "current-review-fingerprint";
        var policy = new CompendiumReadinessPolicy();
        var assessment = policy.Evaluate(new CompendiumProjectReadinessContext(
            1,
            "Example",
            ProjectLifecycleStatus.Active,
            null,
            "Infantry",
            "Description",
            null,
            null,
            10,
            true,
            CompendiumImageSelectionMode.Explicit,
            200,
            false,
            fingerprint,
            fingerprint));

        Assert.True(assessment.IsReviewed);
        Assert.False(assessment.IsReviewStale);
        Assert.DoesNotContain(assessment.Findings, finding => finding.Code == "reviewRequired");
    }

    [Fact]
    public void ReadinessPolicy_InvalidatesReviewWhenFingerprintChanges()
    {
        var policy = new CompendiumReadinessPolicy();
        var assessment = policy.Evaluate(new CompendiumProjectReadinessContext(
            1,
            "Example",
            ProjectLifecycleStatus.Active,
            null,
            "Infantry",
            "Description",
            null,
            null,
            10,
            true,
            CompendiumImageSelectionMode.Explicit,
            200,
            false,
            "new-fingerprint",
            "old-fingerprint"));

        Assert.False(assessment.IsReviewed);
        Assert.True(assessment.IsReviewStale);
        Assert.DoesNotContain(assessment.Findings, finding => finding.Code == "projectChangedAfterReview");
    }
}
