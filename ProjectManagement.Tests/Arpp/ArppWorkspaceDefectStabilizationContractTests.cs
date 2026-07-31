using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppWorkspaceDefectStabilizationContractTests
{
    [Fact]
    public void CollapsingNavigator_LeavesGridMountedAtFullWidth()
    {
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");

        Assert.Contains(".arpp-workspace-v2.is-navigator-collapsed .arpp-workspace-v2__body", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2.is-navigator-collapsed .arpp-workspace-v2__grid-shell", css, StringComparison.Ordinal);
        Assert.Contains("grid-column: 1;", ExtractRule(css, ".arpp-workspace-v2.is-navigator-collapsed .arpp-workspace-v2__grid-shell"), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationCountsAndInvalidRows_UseOneDerivedIssueSet()
    {
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");

        Assert.Contains("const validationRowsFrom = issues => new Set", script, StringComparison.Ordinal);
        Assert.Contains("validationIssueRowCount = issueRows.size;", script, StringComparison.Ordinal);
        Assert.Contains("syncInvalidRowClasses(issueRows);", script, StringComparison.Ordinal);
        Assert.Contains("updateRowNavigator(issues);", script, StringComparison.Ordinal);
        Assert.Contains("tr.has-validation-issue td:nth-child(2)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRow_RefreshesValidationThenScrollsAndFocusesTheNewRow()
    {
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("updateValidationNavigator(false);", script, StringComparison.Ordinal);
        Assert.Contains("scrollRowIntoWorkspace(row, firstControl, false);", script, StringComparison.Ordinal);
        Assert.Contains("target.focus({ preventScroll: true });", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigatorToggle_CommunicatesItsCurrentAction()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("aria-label=\"Hide row navigator\"", markup, StringComparison.Ordinal);
        Assert.Contains("const label = isVisible ? \"Hide row navigator\" : \"Show row navigator\";", script, StringComparison.Ordinal);
        Assert.Contains("toggle.setAttribute(\"aria-label\", label);", script, StringComparison.Ordinal);
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
