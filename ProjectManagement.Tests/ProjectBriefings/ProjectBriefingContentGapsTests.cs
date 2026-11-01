using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingContentGapsTests
{
    [Fact]
    public void CombinedDeck_ReportsEverythingItsSlidesShow()
    {
        var gaps = ProjectBriefingContentGaps.Resolve(
            new[] { EmptyProject(1), CompleteProject(2) },
            Settings(ProjectBriefingPresentationMode.Combined, ProjectBriefingCostMode.Both, ProjectBriefingNarrativeMode.Both));

        Assert.Equal(
            new[]
            {
                ProjectBriefingContentGaps.Status,
                ProjectBriefingContentGaps.CostRd,
                ProjectBriefingContentGaps.ProliferationCost,
                ProjectBriefingContentGaps.Photo,
                ProjectBriefingContentGaps.CapabilityOverview,
                ProjectBriefingContentGaps.ProjectBrief
            },
            gaps[1]);
        Assert.Empty(gaps[2]);
    }

    [Fact]
    public void ExecutiveTableOnly_IgnoresPhotosAndNarrative()
    {
        var gaps = ProjectBriefingContentGaps.Resolve(
            new[] { EmptyProject(1) },
            Settings(ProjectBriefingPresentationMode.ExecutiveTable, ProjectBriefingCostMode.CostRdOnly, ProjectBriefingNarrativeMode.Both));

        Assert.Equal(new[] { ProjectBriefingContentGaps.Status, ProjectBriefingContentGaps.CostRd }, gaps[1]);
    }

    [Fact]
    public void DetailedSlidesWithoutStatus_DoNotAskForAStatusRemark()
    {
        var settings = Settings(ProjectBriefingPresentationMode.DetailedProjects, ProjectBriefingCostMode.None, ProjectBriefingNarrativeMode.CapabilityOverview)
            with { StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default with { ShowPresentStatus = false } };

        var gaps = ProjectBriefingContentGaps.Resolve(new[] { EmptyProject(1) }, settings);

        Assert.Equal(new[] { ProjectBriefingContentGaps.Photo, ProjectBriefingContentGaps.CapabilityOverview }, gaps[1]);
    }

    [Fact]
    public void UpdateSheetsThatHideEmptyRows_OnlyReportStructuralContentAndFacts()
    {
        var settings = Settings(ProjectBriefingPresentationMode.Combined, ProjectBriefingCostMode.Both, ProjectBriefingNarrativeMode.CapabilityOverview)
            with
            {
                Layout = ProjectBriefingLayout.ProjectUpdateSheet,
                UpdateSheetOptions = ProjectBriefingUpdateSheetOptions.Default with { HideEmptyValues = true }
            };

        var gaps = ProjectBriefingContentGaps.Resolve(new[] { EmptyProject(1) }, settings);

        Assert.Equal(
            new[] { ProjectBriefingContentGaps.Photo, ProjectBriefingContentGaps.ProjectBrief, ProjectBriefingContentGaps.UpdateSheetFacts },
            gaps[1]);
    }

    [Fact]
    public void DeletedAndArchivedProjects_AreLeftOut()
    {
        var deleted = new ProjectBriefingProjectVm { ProjectId = 3, IsDeletedOrArchived = true };

        var gaps = ProjectBriefingContentGaps.Resolve(
            new[] { deleted },
            Settings(ProjectBriefingPresentationMode.Combined, ProjectBriefingCostMode.Both, ProjectBriefingNarrativeMode.Both));

        Assert.False(gaps.ContainsKey(3));
    }

    [Theory]
    [InlineData("Capability overview not recorded.", false)]
    [InlineData("Brief description not recorded.", false)]
    [InlineData("  ", false)]
    [InlineData("• Tracks eight targets", true)]
    public void CapabilityPlaceholders_CountAsMissing(string text, bool expected)
        => Assert.Equal(expected, ProjectBriefingContentGaps.HasCapabilityOverview(text));

    private static ProjectBriefingContentGapSettings Settings(
        ProjectBriefingPresentationMode mode,
        ProjectBriefingCostMode costMode,
        ProjectBriefingNarrativeMode narrativeMode)
        => new(
            ProjectBriefingLayout.StandardBriefing,
            mode,
            costMode,
            narrativeMode,
            ProjectBriefingStandardSlideOptions.Default,
            ProjectBriefingUpdateSheetOptions.Default);

    private static ProjectBriefingProjectVm EmptyProject(int id)
        => new()
        {
            ProjectId = id,
            ProjectName = $"Project {id}",
            BriefDescription = "Capability overview not recorded.",
            ProjectBrief = "Project brief not recorded."
        };

    private static ProjectBriefingProjectVm CompleteProject(int id)
        => new()
        {
            ProjectId = id,
            ProjectName = $"Project {id}",
            ExternalStatus = "TEC under progress.",
            CostRd = new ProjectBriefingCostValue(10_000_000m, ProjectBriefingCostBasis.AoN, "₹1 Cr", "AoN"),
            ProliferationCost = new ProjectBriefingCostValue(1_000_000m, ProjectBriefingCostBasis.Proliferation, "₹10 Lakh", "Indicative"),
            HasCoverPhoto = true,
            BriefDescription = "• Tracks eight targets",
            ProjectBrief = "A man-portable system.",
            IsUpdateSheetCoreFactsReady = true
        };
}
