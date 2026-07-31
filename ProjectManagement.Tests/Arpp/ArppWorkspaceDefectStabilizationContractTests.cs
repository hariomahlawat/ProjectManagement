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
    public void NavigatorToggle_CommunicatesStateAndCurrentAction()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("data-arpp-navigator-label", markup, StringComparison.Ordinal);
        Assert.Contains("const actionLabel = isVisible ? \"Hide row navigator\" : \"Show row navigator\";", script, StringComparison.Ordinal);
        Assert.Contains("toggle.setAttribute(\"aria-pressed\", isVisible ? \"true\" : \"false\");", script, StringComparison.Ordinal);
        Assert.Contains("visibleLabel.textContent = isVisible ? \"Rows\" : \"Show rows\";", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationTray_ShowsShortcutOnlyForLongOrFilteredIssueSets()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("data-arpp-validation-actions", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-validation-first", markup, StringComparison.Ordinal);
        Assert.Contains("const showFirstShortcut = groups.length > 5 || firstOutsideCurrentFilter;", script, StringComparison.Ordinal);
        Assert.Contains("firstButton.classList.toggle(\"d-none\", !showFirstShortcut);", script, StringComparison.Ordinal);
        Assert.Contains("actions.classList.toggle(\"d-none\", !hasVisibleAction);", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HorizontalScrollCuesAndActionsBoundary_AreContextual()
    {
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");

        Assert.Contains("tableViewport.classList.toggle(\"has-horizontal-overflow\", hasOverflow);", script, StringComparison.Ordinal);
        Assert.Contains("hasOverflow && tableWrap.scrollLeft > 2", script, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2__table-viewport.is-scrolled-start .arpp-actions-cell", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2__table-viewport::after {\n    right: 4.5rem;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledSaveAndEntryGuidance_AreUnambiguousAndCompact()
    {
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");

        var disabledRule = ExtractRule(css, ".arpp-workspace-v2__header [data-arpp-save-button]:disabled");
        Assert.Contains("cursor: not-allowed;", disabledRule, StringComparison.Ordinal);
        Assert.Contains("opacity: .56;", disabledRule, StringComparison.Ordinal);
        Assert.Contains("entryGuidance.removeAttribute(\"open\");", script, StringComparison.Ordinal);
    }

    private static string ExtractRule(string css, string selector)
    {
        var start = css.LastIndexOf(selector, StringComparison.Ordinal);
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
