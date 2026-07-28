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

    [Fact]
    public void Repository_UsesServerSideOrderingAcrossViewsAndPagination()
    {
        var view = ReadRepoFile("Pages", "Projects", "Index.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "Index.cshtml.cs");
        var query = ReadRepoFile("Services", "Projects", "ProjectSearchFilters.cs");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-index.js");

        Assert.Contains("query.ApplyProjectOrdering(filters, Sort, Dir)", model, StringComparison.Ordinal);
        Assert.Contains("OperationalLifecycleRank", query, StringComparison.Ordinal);
        Assert.Contains("asp-all-route-data=\"SortRoute(ProjectRepositorySort.Project)\"", view, StringComparison.Ordinal);
        Assert.Contains("The order is applied to the complete filtered result before pagination.", view, StringComparison.Ordinal);
        Assert.DoesNotContain("sortTable", script, StringComparison.Ordinal);
        Assert.DoesNotContain("data-project-sort-table", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_UsesFixedResponsiveTracksAndDoesNotStretchSingleResult()
    {
        var css = ReadRepoFile("wwwroot", "css", "projects", "index.css");

        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("repeat(auto-fit", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_UsesSharedCountGrammarAndSemanticCancelledBadge()
    {
        var view = ReadRepoFile("Pages", "Projects", "Index.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "projects", "index.css");

        Assert.Contains("var projectCountLabel = Model.FilteredTotal == 1 ? \"project\" : \"projects\";", view, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(view, "@projectCountLabel"));
        Assert.Contains("ProjectLifecycleStatus.Cancelled => \"project-lifecycle-badge project-lifecycle-badge--cancelled\"", view, StringComparison.Ordinal);
        Assert.Contains(".project-lifecycle-badge--cancelled", css, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectLifecycleStatus.Cancelled => \"text-bg-secondary\"", view, StringComparison.Ordinal);
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
