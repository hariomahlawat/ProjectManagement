using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectOverviewPresentationContractTests
{
    [Fact]
    public void CoverPhoto_UsesLandscapeFourByThreeSurface()
    {
        var css = ReadRepoFile("wwwroot", "css", "pages", "project-portfolio.css");
        var overview = ReadRepoFile("Pages", "Projects", "_ProjectOverviewCard.cshtml");

        Assert.Contains("aspect-ratio: 4 / 3;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("aspect-ratio: 3 / 4;", css, StringComparison.Ordinal);
        Assert.Contains("project-photo-cover-frame--", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeline_PreservesTerminalWorkspaceAndExplainsMissingHistory()
    {
        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");

        Assert.Contains("project-timeline-empty-history", workspace, StringComparison.Ordinal);
        Assert.Contains("No recorded stage history", workspace, StringComparison.Ordinal);
        Assert.Contains("Project remarks remain available", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("No stage history recorded", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("No stage history was recorded", workspace, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workspace, "No recorded stage history"));
    }

    [Fact]
    public void LegacyHeader_DistinguishesRecordMetadataFromHistoricalProjectDates()
    {
        var header = ReadRepoFile("Pages", "Projects", "_ProjectCommandHeader.cshtml");

        Assert.Contains("Mapped workflow", header, StringComparison.Ordinal);
        Assert.Contains("Record added", header, StringComparison.Ordinal);
        Assert.Contains("project?.IsLegacy == true", header, StringComparison.Ordinal);
    }

    [Fact]
    public void RemarksFilters_AreRenderedInsideRemarksBody()
    {
        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");
        var remarksBodyIndex = workspace.IndexOf("data-panel=\"remarks\"", StringComparison.Ordinal);
        var filtersIndex = workspace.IndexOf("remarks-filter-toolbar", StringComparison.Ordinal);

        Assert.True(remarksBodyIndex >= 0, "The remarks body was not found.");
        Assert.True(filtersIndex > remarksBodyIndex, "Remarks filters must be rendered in the body rather than the card header.");
    }

    [Fact]
    public void TotCard_UsesServerToneAndTracksActualDirtyState()
    {
        var card = ReadRepoFile("Pages", "Shared", "Components", "ProjectTotCommandCard", "Default.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "projects", "overview-tot.js");

        Assert.Contains("project-intelligence-card--tone-@Model.Tone", card, StringComparison.Ordinal);
        Assert.Contains("data-tot-card-tone=\"@Model.Tone\"", card, StringComparison.Ordinal);
        Assert.Contains("payload.tone", script, StringComparison.Ordinal);
        Assert.Contains("hasUnsavedDetails()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredPostCompletionPartial_CannotRestoreLowerTotPanel()
    {
        var postCompletion = ReadRepoFile("Pages", "Projects", "_ProjectPostCompletion.cshtml");
        var exploitationTransfer = ReadRepoFile("Pages", "Projects", "_ProjectExploitationTransfer.cshtml");

        Assert.DoesNotContain("_ProjectTotPanel", postCompletion, StringComparison.Ordinal);
        Assert.DoesNotContain("_ProjectTotPanel", exploitationTransfer, StringComparison.Ordinal);
        Assert.Contains("Compatibility placeholder", postCompletion, StringComparison.Ordinal);
        Assert.Contains("Compatibility placeholder", exploitationTransfer, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativePath)}");
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
