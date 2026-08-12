using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochurePhotoPrintQualityEvaluatorTests
{
    [Fact]
    public void PrintCompact_ProjectFrame_WarnsForFourHundredPixelSquareAfterWideCrop()
    {
        var assessment = BrochurePhotoPrintQualityEvaluator.Assess(
            400,
            400,
            BrochurePublicationProfile.PrintCompact,
            BrochurePhotoPrintPlacement.ProjectCard);

        Assert.Equal(400d, assessment.EffectiveWidthPixels, 1);
        Assert.Equal(225d, assessment.EffectiveHeightPixels, 1);
        Assert.InRange(assessment.EffectiveDpi, 184d, 186d);
        Assert.False(assessment.MeetsRecommendation);
    }

    [Fact]
    public void PrintCompact_ProjectFrame_AcceptsOneThousandTwentyFourPixelSquare()
    {
        var assessment = BrochurePhotoPrintQualityEvaluator.Assess(
            1024,
            1024,
            BrochurePublicationProfile.PrintCompact,
            BrochurePhotoPrintPlacement.ProjectFeature);

        Assert.Equal(1024d, assessment.EffectiveWidthPixels, 1);
        Assert.Equal(576d, assessment.EffectiveHeightPixels, 1);
        Assert.True(assessment.EffectiveDpi > 470d);
        Assert.True(assessment.MeetsRecommendation);
    }

    [Fact]
    public void PrintCompact_ProjectFrame_AcceptsOneThousandTwentyFourByFiveFortyWideSource()
    {
        var assessment = BrochurePhotoPrintQualityEvaluator.Assess(
            1024,
            540,
            BrochurePublicationProfile.PrintCompact,
            BrochurePhotoPrintPlacement.ProjectFeature);

        Assert.Equal(960d, assessment.EffectiveWidthPixels, 1);
        Assert.Equal(540d, assessment.EffectiveHeightPixels, 1);
        Assert.True(assessment.EffectiveDpi > 440d);
        Assert.True(assessment.MeetsRecommendation);
    }

    [Fact]
    public void PrintCompact_CoverHero_EvaluatesAgainstFullPhysicalHeroWidth()
    {
        var good = BrochurePhotoPrintQualityEvaluator.Assess(
            1800,
            1055,
            BrochurePublicationProfile.PrintCompact,
            BrochurePhotoPrintPlacement.CoverHero);
        var soft = BrochurePhotoPrintQualityEvaluator.Assess(
            1200,
            700,
            BrochurePublicationProfile.PrintCompact,
            BrochurePhotoPrintPlacement.CoverHero);

        Assert.True(good.EffectiveDpi > 300d);
        Assert.True(good.MeetsRecommendation);
        Assert.True(soft.EffectiveDpi < BrochurePhotoPrintQualityEvaluator.PrintCompactRecommendedDpi);
        Assert.False(soft.MeetsRecommendation);
    }

    [Fact]
    public void DigitalComfortable_UsesScreenFirstEffectiveDpiFloor()
    {
        var assessment = BrochurePhotoPrintQualityEvaluator.Assess(
            1024,
            1024,
            BrochurePublicationProfile.DigitalComfortable,
            BrochurePhotoPrintPlacement.ProjectFeature);

        Assert.Equal(BrochurePhotoPrintQualityEvaluator.DigitalComfortableRecommendedDpi, assessment.RecommendedDpi);
        Assert.True(assessment.EffectiveDpi > 190d);
        Assert.True(assessment.MeetsRecommendation);
    }
    [Fact]
    public void DigitalComfortable_EditorialSplit_EvaluatesAgainstActualLargerSplitFrame()
    {
        var good = BrochurePhotoPrintQualityEvaluator.Assess(
            1024,
            1024,
            BrochurePublicationProfile.DigitalComfortable,
            BrochurePhotoPrintPlacement.ProjectEditorialSplit);
        var soft = BrochurePhotoPrintQualityEvaluator.Assess(
            500,
            500,
            BrochurePublicationProfile.DigitalComfortable,
            BrochurePhotoPrintPlacement.ProjectEditorialSplit);

        Assert.Contains("editorial split", good.PlacementLabel, StringComparison.OrdinalIgnoreCase);
        Assert.True(good.EffectiveDpi > 325d);
        Assert.True(good.MeetsRecommendation);
        Assert.True(soft.EffectiveDpi < BrochurePhotoPrintQualityEvaluator.DigitalComfortableRecommendedDpi);
        Assert.False(soft.MeetsRecommendation);
    }

    [Fact]
    public void DigitalComfortable_CoverHero_UsesPremiumCoverAspectAndPhysicalFrame()
    {
        var good = BrochurePhotoPrintQualityEvaluator.Assess(
            1800,
            1360,
            BrochurePublicationProfile.DigitalComfortable,
            BrochurePhotoPrintPlacement.CoverHero);
        var soft = BrochurePhotoPrintQualityEvaluator.Assess(
            1200,
            907,
            BrochurePublicationProfile.DigitalComfortable,
            BrochurePhotoPrintPlacement.CoverHero);

        Assert.True(good.EffectiveDpi > 235d);
        Assert.True(good.MeetsRecommendation);
        Assert.True(soft.EffectiveDpi < BrochurePhotoPrintQualityEvaluator.DigitalComfortableRecommendedDpi);
        Assert.False(soft.MeetsRecommendation);
    }

}
