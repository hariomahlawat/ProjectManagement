using System;
using System.IO;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectContentPresentationContractTests
{
    [Fact]
    public void SaveClient_UsesExplicitReloadWithRecoveryInsteadOfSameUrlNavigation()
    {
        var script = ReadRepoFile("wwwroot", "js", "projects", "project-content.js");

        Assert.Contains("window.location.reload()", script, StringComparison.Ordinal);
        Assert.Contains("projectcontent:reload-requested", script, StringComparison.Ordinal);
        Assert.Contains("Your changes were saved, but the page did not refresh.", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.location.replace(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.location.assign(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("redirectUrl", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveHandler_ReturnsStableAjaxContractAndScopedConfirmation()
    {
        var pageModel = ReadRepoFile("Pages", "Projects", "Overview.Content.cs");
        var partial = ReadRepoFile("Pages", "Projects", "_ProjectContentTabs.cshtml");

        Assert.Contains("TempData[\"ProjectContentFlash\"] = successMessage;", pageModel, StringComparison.Ordinal);
        Assert.Contains("section = tab", pageModel, StringComparison.Ordinal);
        Assert.DoesNotContain("redirectUrl", pageModel, StringComparison.Ordinal);
        Assert.Contains("TempData[\"ProjectContentFlash\"]", partial, StringComparison.Ordinal);
        Assert.Contains("data-auto-dismiss=\"6000\"", partial, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectBrief_UsesConcise100To150WordGuidanceAnd200WordMaximum()
    {
        var limits = ReadRepoFile("Models", "ProjectFieldLimits.cs");
        var partial = ReadRepoFile("Pages", "Projects", "_ProjectContentTabs.cshtml");

        Assert.Contains("ProjectBriefRecommendedMinimumWords = 100", limits, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefRecommendedMaximumWords = 150", limits, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefHardMaximumWords = 200", limits, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefReadiness.Concise => (\"Concise\", \"is-neutral\")", partial, StringComparison.Ordinal);
        Assert.DoesNotContain("Brief incomplete", partial, StringComparison.Ordinal);
    }

    [Fact]
    public void DescriptionPreview_UsesAuthoritativeServerMarkdownRenderer()
    {
        var pageModel = ReadRepoFile("Pages", "Projects", "Overview.Content.cs");
        var partial = ReadRepoFile("Pages", "Projects", "_ProjectContentTabs.cshtml");

        Assert.Contains("OnPostPreviewProjectDescription", pageModel, StringComparison.Ordinal);
        Assert.Contains("ProjectContentRules.NormalizeNarrative", pageModel, StringComparison.Ordinal);
        Assert.Contains("_markdownRenderer.ToSafeHtml", pageModel, StringComparison.Ordinal);
        Assert.Contains("data-description-preview-url", partial, StringComparison.Ordinal);
        Assert.Contains("Markdown help", partial, StringComparison.Ordinal);
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
}
