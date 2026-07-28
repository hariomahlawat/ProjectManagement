using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class CompletedSummaryPresentationContractTests
{
    [Fact]
    public void Register_IsDefaultAndLegacyPortfolioCardGridIsRemoved()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-completed-summary.js");

        Assert.Contains("data-default-view=\"register\"", view, StringComparison.Ordinal);
        Assert.Contains("data-view-panel=\"register\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("cpw-portfolio-grid", view, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card-sort", view, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card-group", view, StringComparison.Ordinal);
        Assert.Contains("completedProjectsWorkspaceViewV2", script, StringComparison.Ordinal);
        Assert.DoesNotContain("completedProjectsView", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_UsesActionQueuesRatherThanEnumeratingEveryProjectAsACard()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");

        Assert.Contains("Proliferation posture", view, StringComparison.Ordinal);
        Assert.Contains("Available but blocked", view, StringComparison.Ordinal);
        Assert.Contains("Technology action required", view, StringComparison.Ordinal);
        Assert.Contains("ToT action pending", view, StringComparison.Ordinal);
        Assert.Contains("Open filtered register", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-PortfolioStatus", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_ExposesServerSortingAndSeparateQualitySemantics()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml.cs");

        Assert.Contains("aria-sort=\"@nameSort.Aria\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-Sort=\"quality\"", view, StringComparison.Ordinal);
        Assert.Contains("critical", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supplementary", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("or \"quality\"", model, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}
