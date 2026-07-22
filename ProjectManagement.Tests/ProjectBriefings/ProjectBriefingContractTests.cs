using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingContractTests
{
    [Fact]
    public void Builder_UsesApprovedTerminologySelectionMethodsAndExternalStatusPolicy()
    {
        var page = Read("Index.cshtml");

        Assert.Contains("Project Briefing Deck Builder", page, StringComparison.Ordinal);
        Assert.Contains("Briefing decks", Read("_CommandWorkspaceRail.cshtml"), StringComparison.Ordinal);
        Assert.Contains("All ongoing projects", page, StringComparison.Ordinal);
        Assert.Contains("Recently completed", page, StringComparison.Ordinal);
        Assert.Contains("Project category", page, StringComparison.Ordinal);
        Assert.Contains("Technical category", page, StringComparison.Ordinal);
        Assert.Contains("Available for proliferation", page, StringComparison.Ordinal);
        Assert.Contains("Select individually", page, StringComparison.Ordinal);
        Assert.Contains("Status <small>external remark</small>", page, StringComparison.Ordinal);
        Assert.Contains("Cost (R&amp;D)", page, StringComparison.Ordinal);
        Assert.Contains("Generate PowerPoint", page, StringComparison.Ordinal);
        Assert.Contains("Project Update Review", page, StringComparison.Ordinal);
        Assert.Contains("Shared decks", page, StringComparison.Ordinal);
        Assert.Contains("Estimated deck size", page, StringComparison.Ordinal);
        Assert.Contains("chart and table", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_UsesApplicationAntiforgeryHeaderAndDownloadsPptx()
    {
        var script = Read("project-briefing-decks.js");

        Assert.Contains("X-CSRF-TOKEN", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'RequestVerificationToken'", script, StringComparison.Ordinal);
        Assert.Contains("application/vnd.openxmlformats-officedocument.presentationml.presentation", script, StringComparison.Ordinal);
        Assert.Contains("X-Project-Briefing-Slides", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CostResolver_UsesL1ThenAonThenIpaAndKeepsProliferationSeparate()
    {
        var source = Read("ProjectBriefingCostResolver.cs");
        var l1 = source.IndexOf("l1.TryGetValue", StringComparison.Ordinal);
        var aon = source.IndexOf("aon.TryGetValue", StringComparison.Ordinal);
        var ipa = source.IndexOf("ipa.TryGetValue", StringComparison.Ordinal);

        Assert.True(l1 >= 0 && aon > l1 && ipa > aon, "Cost (R&D) resolution must remain L1 → AoN → IPA.");
        Assert.Contains("ProjectProductionCostFacts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.Proliferation", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalStatusResolver_UsesExternalGeneralRemarksOnly()
    {
        var source = Read("ProjectBriefingExternalStatusService.cs");

        Assert.Contains("remark.Type == RemarkType.External", source, StringComparison.Ordinal);
        Assert.Contains("remark.Scope == RemarkScope.General", source, StringComparison.Ordinal);
        Assert.Contains("!remark.IsDeleted", source, StringComparison.Ordinal);
        Assert.Contains("row.LastEditedAtUtc ?? row.CreatedAtUtc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemarkType.Internal", source, StringComparison.Ordinal);
    }


    [Fact]
    public void SharedDecks_AreCommandWorkspaceWideAndTrackLastModifier()
    {
        var source = Read("ProjectBriefingDeckService.cs");

        Assert.DoesNotContain("deck.OwnerUserId ==", source, StringComparison.Ordinal);
        Assert.Contains("LastModifiedByUserId", source, StringComparison.Ordinal);
        Assert.Contains("A shared command deck with this name already exists", source, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(deck => deck.UpdatedAtUtc)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StageSummary_IsOneChartThenNativeTableInReverseWorkflowOrder()
    {
        var dataSource = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("OrderByDescending(point => point.Order)", dataSource, StringComparison.Ordinal);
        Assert.Contains("AddStageSummarySlides", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStageSummaryTable", composer, StringComparison.Ordinal);
        Assert.Contains("Stage-wise project distribution", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("data.Summary.StageSummary.Chunk", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("reverse workflow order", composer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bars are native editable PowerPoint shapes", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("STATUS: LATEST EXTERNAL REMARK ONLY", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTablePagination.Paginate", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTablePagination.Paginate", dataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailedSlide_PrioritisesCapabilityOverviewAndCombinesStageWithStatus()
    {
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("PROJECT POSITION", composer, StringComparison.Ordinal);
        Assert.Contains("CAPABILITY OVERVIEW", composer, StringComparison.Ordinal);
        Assert.Contains("const double rightWidth = 7.48", composer, StringComparison.Ordinal);
        Assert.Contains("var photoHeight = hasPhoto ? 2.48 : 1.18", composer, StringComparison.Ordinal);
        Assert.Contains("FitOverview", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoLoader_ValidatesActualFilesAndProducesPowerPointReadyJpeg()
    {
        var source = Read("ProjectBriefingPhotoLoader.cs");

        Assert.Contains("Image.Identify", source, StringComparison.Ordinal);
        Assert.Contains("ResizeMode.Crop", source, StringComparison.Ordinal);
        Assert.Contains("new JpegEncoder", source, StringComparison.Ordinal);
        Assert.Contains("master/", source, StringComparison.Ordinal);
        Assert.Contains("No PowerPoint-ready photograph was found", source, StringComparison.Ordinal);
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", fileName);
        Assert.True(File.Exists(path), $"Project briefing contract file was not copied to test output: {path}");
        return File.ReadAllText(path);
    }
}
