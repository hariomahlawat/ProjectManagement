using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectRepositoryPresentationContractTests
{
    [Fact]
    public void Repository_UsesServerResolvedLifecyclePositions()
    {
        var results = ReadRepoFile("Pages", "Projects", "_ProjectRepositoryResults.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "Index.cshtml.cs");

        Assert.Contains("Model.StagePositions.TryGetValue", results, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatStageDisplay(", results, StringComparison.Ordinal);
        Assert.Contains("ProjectRepositoryStagePositionVm.Create", model, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_CoverImagesHaveDeterministicFailureFallback()
    {
        var results = ReadRepoFile("Pages", "Projects", "_ProjectRepositoryResults.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-index.js");

        Assert.Contains("data-project-card-cover-image", results, StringComparison.Ordinal);
        Assert.Contains("data-project-card-cover-fallback", results, StringComparison.Ordinal);
        Assert.Contains("image.naturalWidth === 0", script, StringComparison.Ordinal);
        Assert.Contains("project-card__visual--icon", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_HasOneCreateActionAndSeparatesLegacyArchive()
    {
        var view = ReadRepoFile("Pages", "Projects", "Index.cshtml");
        var lifecycle = ReadRepoFile("Pages", "Projects", "_ProjectRepositoryLifecycle.cshtml");

        Assert.DoesNotContain("projects-header__create", view, StringComparison.Ordinal);
        Assert.Contains("projects-archive-filter", lifecycle, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Project lifecycle filters\"", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_UsesServerSideOrderingAcrossViewsAndPagination()
    {
        var results = ReadRepoFile("Pages", "Projects", "_ProjectRepositoryResults.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "Index.cshtml.cs");
        var query = ReadRepoFile("Services", "Projects", "ProjectSearchFilters.cs");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-index.js");

        Assert.Contains("query.ApplyProjectOrdering(filters, Sort, Dir)", model, StringComparison.Ordinal);
        Assert.Contains("OperationalLifecycleRank", query, StringComparison.Ordinal);
        Assert.Contains("asp-all-route-data=\"Model.BuildSortRoute(ProjectRepositorySort.Project)\"", results, StringComparison.Ordinal);
        Assert.Contains("The order is applied to the complete filtered result before pagination.", results, StringComparison.Ordinal);
        Assert.DoesNotContain("sortTable", script, StringComparison.Ordinal);
        Assert.DoesNotContain("data-project-sort-table", results, StringComparison.Ordinal);
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
        var header = ReadRepoFile("Pages", "Projects", "_ProjectRepositoryHeaderSummary.cshtml");
        var results = ReadRepoFile("Pages", "Projects", "_ProjectRepositoryResults.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "Index.cshtml.cs");
        var css = ReadRepoFile("wwwroot", "css", "projects", "index.css");

        Assert.Contains("public string ProjectCountLabel => FilteredTotal == 1 ? \"project\" : \"projects\";", model, StringComparison.Ordinal);
        Assert.Contains("@Model.ProjectCountLabel", header, StringComparison.Ordinal);
        Assert.Contains("@Model.ProjectCountLabel", results, StringComparison.Ordinal);
        Assert.Contains("ProjectLifecycleStatus.Cancelled => \"project-lifecycle-badge project-lifecycle-badge--cancelled\"", model, StringComparison.Ordinal);
        Assert.Contains(".project-lifecycle-badge--cancelled", css, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectLifecycleStatus.Cancelled => \"text-bg-secondary\"", model, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_LiveSearchKeepsTheInputStableAndCancelsStaleRequests()
    {
        var view = ReadRepoFile("Pages", "Projects", "Index.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "Index.cshtml.cs");
        var livePartial = ReadRepoFile("Pages", "Projects", "_ProjectRepositoryLive.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-index.js");

        Assert.Contains("data-project-search", view, StringComparison.Ordinal);
        Assert.Contains("data-project-live-status", view, StringComparison.Ordinal);
        Assert.Contains("OnGetLiveAsync", model, StringComparison.Ordinal);
        Assert.Contains("LoadRepositoryAsync(loadFilterOptions: false)", model, StringComparison.Ordinal);
        Assert.Contains("Partial(\"_ProjectRepositoryLive\", this)", model, StringComparison.Ordinal);
        Assert.Contains("_ProjectRepositoryResults", livePartial, StringComparison.Ordinal);
        Assert.Contains("AbortController", script, StringComparison.Ordinal);
        Assert.Contains("activeController?.abort()", script, StringComparison.Ordinal);
        Assert.Contains("window.fetch", script, StringComparison.Ordinal);
        Assert.Contains("replaceLiveFragments", script, StringComparison.Ordinal);
        Assert.DoesNotContain("requestSubmit", script, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout(submitForm, 450)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_LiveResponseDoesNotReloadStaticFilterOptions()
    {
        var model = ReadRepoFile("Pages", "Projects", "Index.cshtml.cs");

        Assert.Contains("if (loadFilterOptions)", model, StringComparison.Ordinal);
        Assert.Contains("await LoadFilterOptionsAsync(cancellationToken);", model, StringComparison.Ordinal);
        Assert.Contains("loadProjectTypeDefinitions: loadFilterOptions", model, StringComparison.Ordinal);
        Assert.Contains("LoadFilterCountsAsync", model, StringComparison.Ordinal);
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
