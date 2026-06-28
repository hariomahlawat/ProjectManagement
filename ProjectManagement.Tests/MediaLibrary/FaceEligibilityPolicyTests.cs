using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using Xunit;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceEligibilityPolicyTests
{
    [Fact]
    public void Evaluate_AutomaticPhotographMustUseCurrentClassifierAndMeetThreshold()
    {
        var policy = CreatePolicy(0.75);
        var asset = CreateAsset();

        asset.ClassifierVersion = "legacy-v1";
        Assert.False(policy.Evaluate(asset).IsEligible);

        asset.ClassifierVersion = MediaClassifier.ClassifierVersion;
        asset.ClassificationConfidence = 0.74;
        Assert.False(policy.Evaluate(asset).IsEligible);

        asset.ClassificationConfidence = 0.75;
        Assert.True(policy.Evaluate(asset).IsEligible);
    }

    [Fact]
    public void Evaluate_ManualClassificationIsAuthoritative()
    {
        var policy = CreatePolicy(0.95);
        var asset = CreateAsset();
        asset.ClassificationIsManual = true;
        asset.ClassifierVersion = null;
        asset.ClassificationConfidence = null;
        asset.AnalysisStatus = MediaProcessingStatus.NotRequested;

        asset.Classification = MediaClassification.Photograph;
        Assert.True(policy.Evaluate(asset).IsEligible);

        asset.Classification = MediaClassification.Diagram;
        Assert.False(policy.Evaluate(asset).IsEligible);
    }

    [Fact]
    public void Evaluate_PendingAutomaticClassificationIsNotEligible()
    {
        var policy = CreatePolicy(0.50);
        var asset = CreateAsset();
        asset.AnalysisStatus = MediaProcessingStatus.Pending;

        var result = policy.Evaluate(asset);

        Assert.False(result.IsEligible);
        Assert.Equal("classification-pending", result.Code);
    }

    private static FaceEligibilityPolicy CreatePolicy(double threshold)
        => new(Options.Create(new MediaLibraryOptions
        {
            People = new MediaPeopleOptions
            {
                ProcessPhotographsOnly = true,
                MinimumClassificationConfidence = threshold
            }
        }));

    private static MediaAsset CreateAsset()
        => new()
        {
            IsAvailable = true,
            IsDeleted = false,
            IsArchived = false,
            Kind = MediaAssetKind.Photo,
            Classification = MediaClassification.Photograph,
            ClassificationConfidence = 0.90,
            ClassifierVersion = MediaClassifier.ClassifierVersion,
            AnalysisStatus = MediaProcessingStatus.Ready
        };
}
