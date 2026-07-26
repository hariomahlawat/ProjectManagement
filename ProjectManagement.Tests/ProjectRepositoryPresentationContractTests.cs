using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectRepositoryPresentationContractTests
{
    [Fact]
    public void Repository_UsesServerResolvedLifecyclePositions()
    {
        var view = ReadRepoFile("Pages", "Projects", "Index.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "Index.cshtml.cs");

        Assert.Contains("Model.StagePositions.TryGetValue", view, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatStageDisplay(", view, StringComparison.Ordinal);
        Assert.Contains("ProjectRepositoryStagePositionVm.Create", model, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_CoverImagesHaveDeterministicFailureFallback()
    {
        var view = ReadRepoFile("Pages", "Projects", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-index.js");

        Assert.Contains("data-project-card-cover-image", view, StringComparison.Ordinal);
        Assert.Contains("data-project-card-cover-fallback", view, StringComparison.Ordinal);
        Assert.Contains("image.naturalWidth === 0", script, StringComparison.Ordinal);
        Assert.Contains("project-card__visual--icon", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_HasOneCreateActionAndSeparatesLegacyArchive()
    {
        var view = ReadRepoFile("Pages", "Projects", "Index.cshtml");

        Assert.DoesNotContain("projects-header__create", view, StringComparison.Ordinal);
        Assert.Contains("projects-archive-filter", view, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Project lifecycle filters\"", view, StringComparison.Ordinal);
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
