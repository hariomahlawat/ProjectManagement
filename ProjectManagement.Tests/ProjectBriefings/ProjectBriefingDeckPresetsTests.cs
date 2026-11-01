using System.Linq;
using System.Text.Json.Nodes;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingDeckPresetsTests
{
    [Fact]
    public void EveryPreset_MatchesItsOwnSettings()
    {
        Assert.Equal(3, ProjectBriefingDeckPresets.All.Count);
        Assert.All(ProjectBriefingDeckPresets.All, definition =>
            Assert.True(ProjectBriefingDeckPresets.Matches(definition.Preset, definition.State)));
    }

    [Fact]
    public void ChangingASettingThePresetDecides_MarksItAdjusted()
    {
        var monthly = ProjectBriefingDeckPresets.Get(ProjectBriefingDeckPreset.MonthlyCommandReview).State;

        Assert.False(ProjectBriefingDeckPresets.Matches(ProjectBriefingDeckPreset.MonthlyCommandReview, monthly with { CostMode = ProjectBriefingCostMode.None }));
        Assert.False(ProjectBriefingDeckPresets.Matches(ProjectBriefingDeckPreset.MonthlyCommandReview, monthly with { PresentationTheme = ProjectBriefingPresentationTheme.GraphiteDark }));
        var visit = ProjectBriefingDeckPresets.Get(ProjectBriefingDeckPreset.VisitOrExternalAudience).State;
        Assert.False(ProjectBriefingDeckPresets.Matches(ProjectBriefingDeckPreset.VisitOrExternalAudience, visit with { IncludesInstitutionalProfile = false }));
    }

    [Fact]
    public void APresetThatLeavesTheProfileAlone_IsNotAdjustedByIt()
    {
        // Applying Monthly or Update sheets keeps an SDD profile the deck already had.
        var monthly = ProjectBriefingDeckPresets.Get(ProjectBriefingDeckPreset.MonthlyCommandReview).State;
        var sheets = ProjectBriefingDeckPresets.Get(ProjectBriefingDeckPreset.ProjectUpdateSheets).State;

        Assert.True(ProjectBriefingDeckPresets.Matches(ProjectBriefingDeckPreset.MonthlyCommandReview, monthly with { IncludesInstitutionalProfile = true }));
        Assert.True(ProjectBriefingDeckPresets.Matches(ProjectBriefingDeckPreset.ProjectUpdateSheets, sheets with { IncludesInstitutionalProfile = true }));
    }

    [Fact]
    public void SettingsOutsideTheTemplate_DoNotCountAsAdjustments()
    {
        // An update-sheet deck ignores standard-briefing choices; a standard deck ignores sheet rows.
        var sheets = ProjectBriefingDeckPresets.Get(ProjectBriefingDeckPreset.ProjectUpdateSheets).State;
        var monthly = ProjectBriefingDeckPresets.Get(ProjectBriefingDeckPreset.MonthlyCommandReview).State;

        Assert.True(ProjectBriefingDeckPresets.Matches(
            ProjectBriefingDeckPreset.ProjectUpdateSheets,
            sheets with { PresentationMode = ProjectBriefingPresentationMode.ExecutiveTable, CostMode = ProjectBriefingCostMode.None }));
        Assert.True(ProjectBriefingDeckPresets.Matches(
            ProjectBriefingDeckPreset.MonthlyCommandReview,
            monthly with { UpdateSheetRows = new[] { ProjectBriefingUpdateSheetRow.ProjectCost }, HideEmptyUpdateSheetValues = true }));
        Assert.False(ProjectBriefingDeckPresets.Matches(
            ProjectBriefingDeckPreset.ProjectUpdateSheets,
            sheets with { HideEmptyUpdateSheetValues = true }));
    }

    [Fact]
    public void VisitPreset_ShowsTheProfileAndHidesCostsAndStatus()
    {
        var visit = ProjectBriefingDeckPresets.Get(ProjectBriefingDeckPreset.VisitOrExternalAudience).State;

        Assert.True(visit.IncludesInstitutionalProfile);
        Assert.Equal(ProjectBriefingCostMode.None, visit.CostMode);
        Assert.False(visit.ShowPresentStatus);
        Assert.Equal(ProjectBriefingClosingSlideType.ThankYou, visit.ClosingSlideType);
        Assert.Equal(ProjectBriefingPresentationMode.DetailedProjects, visit.PresentationMode);
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
