using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppFinalUiUxCompletionContractTests
{
    [Fact]
    public void Workspace_IsADedicatedSingleViewportApplication()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("class=\"arpp-workspace-v2\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-navigator", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-details-drawer", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-row-filter=\"issues\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-validation-toggle", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("arpp-sticky-left", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("arpp-sticky-right", markup, StringComparison.Ordinal);

        Assert.Contains("position: fixed;", css, StringComparison.Ordinal);
        Assert.Contains("height: 100dvh;", css, StringComparison.Ordinal);
        Assert.Contains("body.arpp-workspace-active", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2__table-wrap", css, StringComparison.Ordinal);
        Assert.Contains("overflow: auto;", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2__table thead th", css, StringComparison.Ordinal);
        Assert.Contains("top: 0;", css, StringComparison.Ordinal);

        Assert.Contains("document.body.classList.add(\"arpp-workspace-active\")", script, StringComparison.Ordinal);
        Assert.Contains("updateRowNavigator", script, StringComparison.Ordinal);
        Assert.Contains("setDetailsOpen", script, StringComparison.Ordinal);
        Assert.Contains("setNavigatorOpen", script, StringComparison.Ordinal);
        Assert.Contains("setSaveButtonsDisabled(saving || !dirty)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordReader_UsesTaskBasedTabsAndNonStickyReadOnlyTable()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Details.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-redesign.css");

        Assert.Contains("arpp-record-v2__tabs", markup, StringComparison.Ordinal);
        Assert.Contains("id=\"arpp-overview\"", markup, StringComparison.Ordinal);
        Assert.Contains("id=\"arpp-rows\"", markup, StringComparison.Ordinal);
        Assert.Contains("id=\"arpp-document\"", markup, StringComparison.Ordinal);
        Assert.Contains("id=\"arpp-audit\"", markup, StringComparison.Ordinal);
        Assert.Contains("<colgroup>", markup, StringComparison.Ordinal);
        Assert.Contains("arpp-v2-project-cell", markup, StringComparison.Ordinal);
        Assert.Contains("sameProjectName", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("position: sticky", ExtractRule(css, ".arpp-v2-table thead th"), StringComparison.Ordinal);
    }

    [Fact]
    public void Module_UsesOneNavigationLanguageAcrossUserJourneys()
    {
        var nav = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "_ModuleNav.cshtml");
        var register = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml");
        var library = ReadRepoFile("Pages", "Projects", "Arpp", "Index.cshtml");

        Assert.Contains("Published", nav, StringComparison.Ordinal);
        Assert.Contains("Administration", nav, StringComparison.Ordinal);
        Assert.Contains("Reconciliation", nav, StringComparison.Ordinal);
        Assert.Contains("ARPP administration", register, StringComparison.Ordinal);
        Assert.Contains("Unlocked for correction", register, StringComparison.Ordinal);
        Assert.Contains("Setup incomplete", register, StringComparison.Ordinal);
        Assert.Contains("_ModuleNav.cshtml", library, StringComparison.Ordinal);
        Assert.DoesNotContain("Manage ARPP", library, StringComparison.Ordinal);
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
