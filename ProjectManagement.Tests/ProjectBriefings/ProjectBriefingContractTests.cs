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

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", fileName);
        Assert.True(File.Exists(path), $"Project briefing contract file was not copied to test output: {path}");
        return File.ReadAllText(path);
    }
}
