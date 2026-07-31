using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectRemarkDefaultTypeContractTests
{
    [Fact]
    public void ProjectRemarkComposers_RenderTheServerResolvedDefaultType()
    {
        var standalone = ReadRepoFile("Pages", "Projects", "Remarks", "Index.cshtml");
        var workspace = ReadRepoFile("Pages", "Projects", "_ProjectLifecycleWorkspace.cshtml");

        Assert.Contains("Model.RemarksPanel.DefaultType", standalone, StringComparison.Ordinal);
        Assert.Contains("defaultConferenceRemark", standalone, StringComparison.Ordinal);
        Assert.Contains("defaultConferenceRemark", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "class=\"btn btn-outline-secondary active\" data-remarks-composer-option=\"Internal\"",
            standalone,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "class=\"btn btn-outline-secondary active\" data-remarks-composer-option=\"Internal\"",
            workspace,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RemarksClientInitialisesAndResetsToTheConfiguredDefaultType()
    {
        var script = ReadRepoFile("wwwroot", "js", "projects", "remarks-panel.js");

        Assert.Contains(
            "this.defaultComposerType = this.resolveComposerType(this.config.defaultType);",
            script,
            StringComparison.Ordinal);
        Assert.Contains("resolveComposerType(type)", script, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(script, "this.setComposerType(this.defaultComposerType);"));
    }

    [Fact]
    public void DefaultTypePolicyIsResolvedOnTheServerForCommandantOnly()
    {
        var viewModel = ReadRepoFile("ViewModels", "ProjectRemarksPanelViewModel.cs");
        var service = ReadRepoFile("Services", "Projects", "ProjectRemarksPanelService.cs");

        Assert.Contains("public string DefaultType", viewModel, StringComparison.Ordinal);
        Assert.Contains("SelectDefaultRemarkType(actorRole, allowConference)", service, StringComparison.Ordinal);
        Assert.Contains("actorRole == RemarkActorRole.Commandant && allowConference", service, StringComparison.Ordinal);
        Assert.Contains("? RemarkType.Conference", service, StringComparison.Ordinal);
        Assert.Contains(": RemarkType.Internal", service, StringComparison.Ordinal);
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

        throw new FileNotFoundException(
            $"Could not locate repository file: {Path.Combine(relativePath)}");
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
