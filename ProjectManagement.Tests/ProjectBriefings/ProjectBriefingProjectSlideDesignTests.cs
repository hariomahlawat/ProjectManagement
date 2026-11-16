using System.Text.Json.Nodes;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// The project slide design (docs/ux/briefing-decks.md, blocks plan PR 5): a card in the Deck tab
// that sets the deck's existing settings, plus the one new setting, Key points only.
public sealed class ProjectBriefingProjectSlideDesignTests
{
    [Theory]
    [InlineData(ProjectBriefingProjectSlideDesign.Capability)]
    [InlineData(ProjectBriefingProjectSlideDesign.KeyPoints)]
    [InlineData(ProjectBriefingProjectSlideDesign.Brief)]
    [InlineData(ProjectBriefingProjectSlideDesign.FactSheet)]
    [InlineData(ProjectBriefingProjectSlideDesign.KeyPointsOnly)]
    [InlineData(ProjectBriefingProjectSlideDesign.CapabilityAndBrief)]
    public void EachDesign_ReadsBackFromTheSettingsItWrites(ProjectBriefingProjectSlideDesign design)
    {
        var settings = ProjectBriefingProjectSlideDesigns.Settings(
            design,
            ProjectBriefingNarrativeMode.CapabilityOverview,
            ProjectBriefingKeyPointPlacement.None,
            showKeyPoints: false,
            ProjectBriefingKeyPointPlacement.ProjectBriefSlide);
        var options = ProjectBriefingStandardSlideOptions.Normalize(
            ProjectBriefingProjectBriefLayout.Automatic,
            showPresentStage: true,
            showPresentStatus: true,
            includeStageDistributionTable: false,
            settings.KeyPoints,
            settings.KeyPointsOnly);

        Assert.Equal(design, ProjectBriefingProjectSlideDesigns.Of(settings.Layout, settings.NarrativeMode, options));
    }

    [Fact]
    public void Brief_KeepsTheKeyPointBand_AndCapabilityAndBriefKeepsItsSlideChoice()
    {
        var brief = ProjectBriefingProjectSlideDesigns.Settings(
            ProjectBriefingProjectSlideDesign.Brief,
            ProjectBriefingNarrativeMode.CapabilityOverview,
            ProjectBriefingKeyPointPlacement.None,
            showKeyPoints: true,
            ProjectBriefingKeyPointPlacement.ProjectBriefSlide);
        Assert.Equal(ProjectBriefingKeyPointPlacement.ProjectBriefSlide, brief.KeyPoints);

        var both = ProjectBriefingProjectSlideDesigns.Settings(
            ProjectBriefingProjectSlideDesign.CapabilityAndBrief,
            ProjectBriefingNarrativeMode.Both,
            ProjectBriefingKeyPointPlacement.CapabilitySlide,
            showKeyPoints: true,
            ProjectBriefingKeyPointPlacement.CapabilitySlide);
        Assert.Equal(ProjectBriefingNarrativeMode.Both, both.NarrativeMode);
        Assert.Equal(ProjectBriefingKeyPointPlacement.CapabilitySlide, both.KeyPoints);
    }

    [Fact]
    public void FactSheet_KeepsTheDecksOtherSettings_ForWhenItChangesBack()
    {
        var sheet = ProjectBriefingProjectSlideDesigns.Settings(
            ProjectBriefingProjectSlideDesign.FactSheet,
            ProjectBriefingNarrativeMode.ProjectBrief,
            ProjectBriefingKeyPointPlacement.ProjectBriefSlide,
            showKeyPoints: false,
            ProjectBriefingKeyPointPlacement.ProjectBriefSlide);

        Assert.Equal(ProjectBriefingLayout.ProjectUpdateSheet, sheet.Layout);
        Assert.Equal(ProjectBriefingNarrativeMode.ProjectBrief, sheet.NarrativeMode);
        Assert.Equal(ProjectBriefingKeyPointPlacement.ProjectBriefSlide, sheet.KeyPoints);
        Assert.False(sheet.KeyPointsOnly);
    }

    [Fact]
    public void AnUnknownDesign_IsRefused()
        => Assert.Throws<InvalidOperationException>(() => ProjectBriefingProjectSlideDesigns.Settings(
            (ProjectBriefingProjectSlideDesign)99,
            ProjectBriefingNarrativeMode.CapabilityOverview,
            ProjectBriefingKeyPointPlacement.None,
            showKeyPoints: false,
            ProjectBriefingKeyPointPlacement.ProjectBriefSlide));

    [Fact]
    public void KeyPointsOnly_IsStored_AndAnOlderBuildReadsTheKeyPointsDesign()
    {
        var json = ProjectBriefingDeckConfigurationCodec.WithStandardSlideOptions(
            null,
            ProjectBriefingStandardSlideOptions.Default with
            {
                KeyPoints = ProjectBriefingKeyPointPlacement.CapabilitySlide,
                KeyPointsOnly = true
            });

        var options = ProjectBriefingDeckConfigurationCodec.Read(json).StandardSlideOptions;
        Assert.True(options.KeyPointsOnly);
        Assert.Equal(
            ProjectBriefingProjectSlideDesign.KeyPointsOnly,
            ProjectBriefingProjectSlideDesigns.Of(ProjectBriefingLayout.StandardBriefing, ProjectBriefingNarrativeMode.CapabilityOverview, options));

        // The settings an older build reads are key points on the capability slide.
        var document = JsonNode.Parse(json)!.AsObject();
        var standard = document["standardBriefing"]!.AsObject();
        Assert.Equal("CapabilitySlide", standard["keyPoints"]!.GetValue<string>());
        standard.Remove("keyPointsOnly");
        var older = ProjectBriefingDeckConfigurationCodec.Read(document.ToJsonString()).StandardSlideOptions;
        Assert.False(older.KeyPointsOnly);
    }

    [Fact]
    public void KeyPointsOnly_NeedsKeyPointsOnTheCapabilitySlide()
    {
        var options = ProjectBriefingStandardSlideOptions.Normalize(
            ProjectBriefingProjectBriefLayout.Automatic,
            showPresentStage: true,
            showPresentStatus: true,
            includeStageDistributionTable: false,
            ProjectBriefingKeyPointPlacement.None,
            keyPointsOnly: true);

        Assert.False(options.KeyPointsOnly);
    }

    [Fact]
    public void KeyPointsOnly_ListsMissingKeyPoints_ButNotMissingCapabilityText()
    {
        var project = new ProjectBriefingProjectVm
        {
            ProjectId = 7,
            ExternalStatus = "Trials complete",
            HasCoverPhoto = true
        };
        var settings = new ProjectBriefingContentGapSettings(
            ProjectBriefingLayout.StandardBriefing,
            ProjectBriefingPresentationMode.DetailedProjects,
            ProjectBriefingCostMode.None,
            ProjectBriefingNarrativeMode.CapabilityOverview,
            ProjectBriefingStandardSlideOptions.Default with
            {
                KeyPoints = ProjectBriefingKeyPointPlacement.CapabilitySlide,
                KeyPointsOnly = true
            },
            ProjectBriefingUpdateSheetOptions.Default);

        var gaps = ProjectBriefingContentGaps.Resolve(new[] { project }, settings)[7];

        Assert.Contains(ProjectBriefingContentGaps.KeyPoints, gaps);
        Assert.DoesNotContain(ProjectBriefingContentGaps.CapabilityOverview, gaps);

        var keyPointsDesign = settings with
        {
            StandardSlideOptions = settings.StandardSlideOptions with { KeyPointsOnly = false }
        };
        Assert.Contains(
            ProjectBriefingContentGaps.CapabilityOverview,
            ProjectBriefingContentGaps.Resolve(new[] { project }, keyPointsDesign)[7]);
    }
}
