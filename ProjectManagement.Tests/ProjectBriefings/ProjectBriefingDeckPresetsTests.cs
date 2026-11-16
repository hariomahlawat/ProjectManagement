using System.Linq;
using System.Text.Json.Nodes;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingDeckPresetsTests
{
    [Fact]
    public void TheTemplatesOffered_AreTheTwoSlideDesigns()
    {
        // A template is a slide design; the visit preset was only settings and is not offered.
        Assert.Equal(
            new[] { ("Portfolio", ProjectBriefingLayout.StandardBriefing), ("Fact Sheets", ProjectBriefingLayout.ProjectUpdateSheet) },
            ProjectBriefingDeckPresets.Templates.Select(template => (template.Title, template.State.Layout)));
        Assert.False(ProjectBriefingDeckPresets.IsTemplate(ProjectBriefingDeckPreset.VisitOrExternalAudience));
    }

    [Theory]
    [InlineData(null, ProjectBriefingLayout.StandardBriefing, ProjectBriefingDeckPreset.MonthlyCommandReview)]
    [InlineData(null, ProjectBriefingLayout.ProjectUpdateSheet, ProjectBriefingDeckPreset.ProjectUpdateSheets)]
    [InlineData(ProjectBriefingDeckPreset.VisitOrExternalAudience, ProjectBriefingLayout.StandardBriefing, ProjectBriefingDeckPreset.MonthlyCommandReview)]
    [InlineData(ProjectBriefingDeckPreset.MonthlyCommandReview, ProjectBriefingLayout.ProjectUpdateSheet, ProjectBriefingDeckPreset.ProjectUpdateSheets)]
    [InlineData(ProjectBriefingDeckPreset.ProjectUpdateSheets, ProjectBriefingLayout.ProjectUpdateSheet, ProjectBriefingDeckPreset.ProjectUpdateSheets)]
    public void EveryDeck_ShowsTheTemplateForItsSlideDesign(
        ProjectBriefingDeckPreset? stored, ProjectBriefingLayout layout, ProjectBriefingDeckPreset expected)
    {
        // Decks set up by hand or from the retired visit preset show the template of their design.
        Assert.Equal(expected, ProjectBriefingDeckPresets.TemplateOf(stored, layout));
    }

    [Fact]
    public void Codec_RemembersThePresetAndKeepsItThroughOtherSaves()
    {
        var json = ProjectBriefingDeckConfigurationCodec.WithPreset(null, ProjectBriefingDeckPreset.VisitOrExternalAudience);
        Assert.Equal("VisitOrExternalAudience", JsonNode.Parse(json)!["preset"]!.GetValue<string>());

        // Saving other settings keeps the remembered preset.
        json = ProjectBriefingDeckConfigurationCodec.WithStandardSlideOptions(
            json,
            ProjectBriefingStandardSlideOptions.Default with { ShowPresentStatus = false });
        Assert.Equal(ProjectBriefingDeckPreset.VisitOrExternalAudience, ProjectBriefingDeckConfigurationCodec.Read(json).Preset);

        json = ProjectBriefingDeckConfigurationCodec.WithPreset(json, null);
        Assert.Null(ProjectBriefingDeckConfigurationCodec.Read(json).Preset);
        Assert.Null(JsonNode.Parse(json)!["preset"]);
    }

    [Fact]
    public void Codec_IgnoresAnUnknownPreset()
    {
        var json = ProjectBriefingDeckConfigurationCodec.WithPreset(null, ProjectBriefingDeckPreset.MonthlyCommandReview)
            .Replace("MonthlyCommandReview", "SomethingNewer");

        var configuration = ProjectBriefingDeckConfigurationCodec.Read(json);

        Assert.Null(configuration.Preset);
        Assert.Equal(ProjectBriefingStandardSlideOptions.Default, configuration.StandardSlideOptions);
    }
}
