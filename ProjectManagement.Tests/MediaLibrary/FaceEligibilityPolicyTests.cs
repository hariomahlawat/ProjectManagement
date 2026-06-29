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
        var asset = CreateAutomaticAsset();

        asset.ClassifierVersion = "legacy-v1";
        Assert.False(policy.Evaluate(asset).IsEligible);

        asset.ClassifierVersion = MediaClassifier.ClassifierVersion;
        asset.PredictedClassificationScore = 0.74m;
        Assert.False(policy.Evaluate(asset).IsEligible);

        asset.PredictedClassificationScore = 0.75m;
        Assert.True(policy.Evaluate(asset).IsEligible);
    }

    [Fact]
    public void Evaluate_ManualPhotographRequiresSeparateFaceAdmission()
    {
        var policy = CreatePolicy(0.95);
        var asset = CreateAutomaticAsset();
        asset.ClassificationIsManual = true;
        asset.ClassifierVersion = null;
        asset.ClassificationConfidence = 1;
        asset.AnalysisStatus = MediaProcessingStatus.Ready;
        asset.Classification = MediaClassification.Photograph;
        asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.ManuallyConfirmed;

        var pending = policy.Evaluate(asset);
        Assert.False(pending.IsEligible);
        Assert.Equal("manual-face-admission-pending", pending.Code);

        asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.ManualFaceProcessingApproved;
        var approved = policy.Evaluate(asset);
        Assert.True(approved.IsEligible);
        Assert.Equal("manual-face-admission", approved.Code);

        asset.Classification = MediaClassification.Diagram;
        Assert.False(policy.Evaluate(asset).IsEligible);
    }

    [Fact]
    public void Evaluate_AutomaticPhotographRequiresSafetyDecisionOutcome()
    {
        var policy = CreatePolicy(0.50);
        var asset = CreateAutomaticAsset();

        asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.NeedsReview;
        Assert.False(policy.Evaluate(asset).IsEligible);

        asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.AutomaticallyAccepted;
        asset.ClassificationDecisionReasonCode = "BELOW_CATEGORY_THRESHOLD";
        Assert.False(policy.Evaluate(asset).IsEligible);

        asset.ClassificationDecisionReasonCode = "AUTO_ACCEPTED_HIGH_CONFIDENCE";
        Assert.True(policy.Evaluate(asset).IsEligible);
    }

    [Fact]
    public void Evaluate_PendingAutomaticClassificationIsNotEligible()
    {
        var policy = CreatePolicy(0.50);
        var asset = CreateAutomaticAsset();
        asset.AnalysisStatus = MediaProcessingStatus.Pending;

        var result = policy.Evaluate(asset);

        Assert.False(result.IsEligible);
        Assert.Equal("classification-pending", result.Code);
    }

    [Fact]
    public void DecisionStatusNamesFitPersistedColumn()
    {
        Assert.All(
            Enum.GetNames<MediaClassificationDecisionStatus>(),
            name => Assert.True(name.Length <= 32, $"Decision status '{name}' exceeds the persisted 32-character limit."));
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

    private static MediaAsset CreateAutomaticAsset()
        => new()
        {
            IsAvailable = true,
            IsDeleted = false,
            IsArchived = false,
            Kind = MediaAssetKind.Photo,
            Classification = MediaClassification.Photograph,
            ClassificationConfidence = 0.90,
            PredictedClassification = MediaClassification.Photograph,
            PredictedClassificationScore = 0.90m,
            ClassificationDecisionStatus = MediaClassificationDecisionStatus.AutomaticallyAccepted,
            ClassificationDecisionReasonCode = "AUTO_ACCEPTED_HIGH_CONFIDENCE",
            ClassifierVersion = MediaClassifier.ClassifierVersion,
            AnalysisStatus = MediaProcessingStatus.Ready
        };
}
