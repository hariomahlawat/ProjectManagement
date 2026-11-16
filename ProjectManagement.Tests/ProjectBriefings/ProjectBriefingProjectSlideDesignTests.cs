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

    // The Deck tab's card rules (review 2 fixes, PR 5): the page reads them from the card, so they
    // must agree with the settings each design writes.
    [Theory]
    [InlineData(ProjectBriefingProjectSlideDesign.Capability)]
    [InlineData(ProjectBriefingProjectSlideDesign.KeyPoints)]
    [InlineData(ProjectBriefingProjectSlideDesign.Brief)]
    [InlineData(ProjectBriefingProjectSlideDesign.FactSheet)]
    [InlineData(ProjectBriefingProjectSlideDesign.KeyPointsOnly)]
    [InlineData(ProjectBriefingProjectSlideDesign.CapabilityAndBrief)]
    public void EachDesignsCardRules_AgreeWithTheSettingsItWrites(ProjectBriefingProjectSlideDesign design)
    {
        foreach (var showKeyPoints in new[] { false, true })
        {
            var settings = ProjectBriefingProjectSlideDesigns.Settings(
                design,
                ProjectBriefingNarrativeMode.CapabilityOverview,
                ProjectBriefingKeyPointPlacement.None,
                showKeyPoints,
                ProjectBriefingKeyPointPlacement.ProjectBriefSlide);
            Assert.Equal(settings.Layout, ProjectBriefingProjectSlideDesigns.Layout(design));
            if (design == ProjectBriefingProjectSlideDesign.FactSheet)
            {
                continue;
            }

            var keyPointsOn = settings.KeyPoints != ProjectBriefingKeyPointPlacement.None;
            var whenChosen = ProjectBriefingProjectSlideDesigns.KeyPointsWhenChosen(design);
            Assert.Equal(whenChosen ?? showKeyPoints, keyPointsOn);
            Assert.Equal(
                settings.NarrativeMode is ProjectBriefingNarrativeMode.ProjectBrief or ProjectBriefingNarrativeMode.Both,
                ProjectBriefingProjectSlideDesigns.UsesBriefLayout(design));
            Assert.Equal(
                settings.NarrativeMode == ProjectBriefingNarrativeMode.Both,
                ProjectBriefingProjectSlideDesigns.OffersKeyPointSlide(design));
        }

        // A fact sheet hides the chips, so it has no hint under them.
        Assert.Equal(
            design != ProjectBriefingProjectSlideDesign.FactSheet,
            ProjectBriefingProjectSlideDesigns.Hint(design).Length > 0);
    }

    [Fact]
    public void TheKeyPointChip_SwapsCapabilityAndKeyPoints_AndLocksKeyPointsOnly()
    {
        Assert.Equal(ProjectBriefingProjectSlideDesign.KeyPoints,
            ProjectBriefingProjectSlideDesigns.WithKeyPoints(ProjectBriefingProjectSlideDesign.Capability, on: true));
        Assert.Equal(ProjectBriefingProjectSlideDesign.Capability,
            ProjectBriefingProjectSlideDesigns.WithKeyPoints(ProjectBriefingProjectSlideDesign.KeyPoints, on: false));
        Assert.Equal(ProjectBriefingProjectSlideDesign.Brief,
            ProjectBriefingProjectSlideDesigns.WithKeyPoints(ProjectBriefingProjectSlideDesign.Brief, on: true));
        Assert.True(ProjectBriefingProjectSlideDesigns.KeyPointsLocked(ProjectBriefingProjectSlideDesign.KeyPointsOnly));
        Assert.False(ProjectBriefingProjectSlideDesigns.KeyPointsLocked(ProjectBriefingProjectSlideDesign.KeyPoints));
    }

    [Theory]
    [InlineData(ProjectBriefingNarrativeMode.CapabilityOverview, true, ProjectBriefingKeyPointPlacement.ProjectBriefSlide, ProjectBriefingKeyPointPlacement.CapabilitySlide)]
    [InlineData(ProjectBriefingNarrativeMode.ProjectBrief, true, ProjectBriefingKeyPointPlacement.CapabilitySlide, ProjectBriefingKeyPointPlacement.ProjectBriefSlide)]
    [InlineData(ProjectBriefingNarrativeMode.Both, true, ProjectBriefingKeyPointPlacement.CapabilitySlide, ProjectBriefingKeyPointPlacement.CapabilitySlide)]
    [InlineData(ProjectBriefingNarrativeMode.Both, true, ProjectBriefingKeyPointPlacement.None, ProjectBriefingKeyPointPlacement.ProjectBriefSlide)]
    [InlineData(ProjectBriefingNarrativeMode.Both, false, ProjectBriefingKeyPointPlacement.CapabilitySlide, ProjectBriefingKeyPointPlacement.None)]
    public void KeyPointPlacement_GoesOnTheOnlyProjectSlide_OrTheChosenOne(
        ProjectBriefingNarrativeMode narrativeMode,
        bool showKeyPoints,
        ProjectBriefingKeyPointPlacement keyPointSlide,
        ProjectBriefingKeyPointPlacement expected)
        => Assert.Equal(expected, ProjectBriefingProjectSlideDesigns.KeyPointPlacement(narrativeMode, showKeyPoints, keyPointSlide));

    [Fact]
    public void Resolve_WithoutAPostedDesign_ReadsTheDesignFromThePostedFields()
    {
        var settings = ProjectBriefingProjectSlideDesigns.Resolve(
            design: null,
            ProjectBriefingLayout.StandardBriefing,
            ProjectBriefingNarrativeMode.CapabilityOverview,
            showKeyPoints: true,
            ProjectBriefingKeyPointPlacement.ProjectBriefSlide);

        Assert.Equal(ProjectBriefingKeyPointPlacement.CapabilitySlide, settings.KeyPoints);
        Assert.False(settings.KeyPointsOnly);
        Assert.Equal(ProjectBriefingNarrativeMode.CapabilityOverview, settings.NarrativeMode);
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
        var json = CodecTestWrites.WithStandardSlideOptions(
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
