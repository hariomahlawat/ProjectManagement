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
        Assert.Contains("Technology review required", view, StringComparison.Ordinal);
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
        Assert.Contains("GetRoutesForSort(\"quality\"", view, StringComparison.Ordinal);
        Assert.Contains("critical", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supplementary", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("or \"quality\"", model, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_UsesNaturalPageFlowAndProgressivelyRevealsWideScreenColumns()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml.cs");
        var css = ReadRepoFile("wwwroot", "css", "pages", "projects-completed-summary.css");

        Assert.Contains("ViewData[\"PageShell\"] = \"workspace\"", view, StringComparison.Ordinal);
        Assert.Contains("cpw-wide-column", view, StringComparison.Ordinal);
        Assert.Contains("cpw-ultrawide-column", view, StringComparison.Ordinal);
        Assert.Contains("CompletedYearOptions", view, StringComparison.Ordinal);
        Assert.Contains("GetRoutesForSort", view, StringComparison.Ordinal);
        Assert.Contains("or \"category\" or \"build\"", model, StringComparison.Ordinal);
        Assert.Contains("max-height: none", css, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 1760px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 2160px)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void WideScreenColumns_AreBackedByServiceAndExport()
    {
        var service = ReadRepoFile("Services", "Projects", "CompletedProjectsSummaryService.cs");
        var export = ReadRepoFile("Utilities", "Reporting", "CompletedProjectsSummaryExcelBuilder.cs");

        Assert.Contains("Include(p => p.TechnicalCategory)", service, StringComparison.Ordinal);
        Assert.Contains("TechnicalCategoryName = p.TechnicalCategory?.Name", service, StringComparison.Ordinal);
        Assert.Contains("BuildType = p.IsBuild ? \"Rebuild\" : \"New\"", service, StringComparison.Ordinal);
        Assert.Contains("\"Technical category\"", export, StringComparison.Ordinal);
        Assert.Contains("\"Build type\"", export, StringComparison.Ordinal);
        Assert.Contains("private const int ColumnCount = 15", export, StringComparison.Ordinal);
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
