using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppProductionFinishingContractTests
{
    [Fact]
    public void PrintView_UsesProfessionalSummaryStatusAndNineColumnTable()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Print.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-print.css");

        Assert.Contains("arpp-print-summary__categories", markup, StringComparison.Ordinal);
        Assert.Contains("Working-copy status:", markup, StringComparison.Ordinal);
        Assert.Contains("Published position:", markup, StringComparison.Ordinal);
        Assert.Contains("Revision @(Model.Issue.PublishedRevisionNumber", markup, StringComparison.Ordinal);
        Assert.Contains("<col class=\"arpp-print-col--project\" />", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>PRISM project</th>", markup, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(4, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-print-table thead { display: table-header-group; }", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_KeepsIssuesCompactNavigatorCollapsibleAndActionsReachable()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("placeholder=\"Find row or project\"", markup, StringComparison.Ordinal);
        Assert.Contains("arpp-workspace-v2__issues-toggle", markup, StringComparison.Ordinal);
        Assert.Contains("Show or hide the row navigator", markup, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2.is-navigator-collapsed", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2__table .arpp-actions-cell", css, StringComparison.Ordinal);
        Assert.Contains("position: sticky;", ExtractRule(css, ".arpp-workspace-v2__table .arpp-col-actions,"), StringComparison.Ordinal);
        Assert.Contains("navigatorStorageKey", script, StringComparison.Ordinal);
        Assert.Contains("restoreNavigatorLayout", script, StringComparison.Ordinal);
        Assert.DoesNotContain("No unsaved changes ·", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationPages_RemoveRedundancyAndUseClearLabels()
    {
        var library = ReadRepoFile("Pages", "Projects", "Arpp", "Index.cshtml");
        var register = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml");
        var record = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Details.cshtml");

        Assert.Contains("placeholder=\"Search ARPP records\"", library, StringComparison.Ordinal);
        Assert.Contains("bi bi-search", library, StringComparison.Ordinal);
        Assert.Contains("Active published records", register, StringComparison.Ordinal);
        Assert.Contains("Records under work", register, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page=\"/ARPP/Reconcile\" class=\"btn", register, StringComparison.Ordinal);
        Assert.Contains("arpp-record-v2__identity-strip", record, StringComparison.Ordinal);
    }

    private static string ExtractRule(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = css.IndexOf('}', start);
        return end < 0 ? css[start..] : css[start..(end + 1)];
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var root = ResolveRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}
