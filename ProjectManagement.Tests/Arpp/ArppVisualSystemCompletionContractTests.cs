using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppVisualSystemCompletionContractTests
{
    [Fact]
    public void AllArppSurfaces_LoadTheSharedTokenLayer()
    {
        var files = new[]
        {
            ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml"),
            ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Create.cshtml"),
            ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Details.cshtml"),
            ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml"),
            ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Reconcile.cshtml"),
            ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "ProjectHistory.cshtml"),
            ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Print.cshtml"),
            ReadRepoFile("Pages", "Projects", "Arpp", "Index.cshtml")
        };

        Assert.All(files, markup => Assert.Contains("arpp-tokens.css", markup, StringComparison.Ordinal));
    }

    [Fact]
    public void TokenLayer_DefinesClearCanvasSurfaceBorderAndTextHierarchy()
    {
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-tokens.css");

        Assert.Contains("--arpp-canvas: #f4f6fa;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--arpp-surface: #ffffff;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--arpp-surface-muted: #eef2f7;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--arpp-border-strong: #c9d2df;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--arpp-text-muted: #667085;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[data-bs-theme=\"dark\"]", css, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordReader_UsesCompactTabsAndDoesNotRepeatIssuedRowsHeading()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Details.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-redesign.css");

        Assert.Contains("Back to administration", markup, StringComparison.Ordinal);
        Assert.Contains("arpp-record-v2__tab-intro", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<div class=\"arpp-record-v2__panel-header\"><div><h2>Issued rows</h2>", markup, StringComparison.Ordinal);
        Assert.Contains(".arpp-record-v2__tab-intro", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.55rem;", ExtractRule(css, ".arpp-record-v2__tabs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_UsesWhiteRowsStrongControlsAndEdgeScrollCues()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");

        Assert.Contains("bi bi-search", markup, StringComparison.Ordinal);
        Assert.Contains("background: var(--arpp-surface);", ExtractRule(css, ".arpp-workspace-v2__table tbody td"), StringComparison.Ordinal);
        Assert.Contains("border-color: var(--arpp-border-strong);", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2__table-viewport.is-scrolled-start::before", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2__table-viewport.is-scrolled-end::after", css, StringComparison.Ordinal);
        Assert.Contains("display: none;", ExtractRule(css, ".arpp-workspace-v2__navigator-header .btn"), StringComparison.Ordinal);
    }

    [Fact]
    public void PrintView_LabelsWorkingValuesAndExposesIncompleteWorkingCopy()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Print.cshtml");

        Assert.Contains("@valueStateLabel approved value", markup, StringComparison.Ordinal);
        Assert.Contains("@valueStateLabel delisted value", markup, StringComparison.Ordinal);
        Assert.Contains("Working-copy completeness:", markup, StringComparison.Ordinal);
        Assert.Contains("DisplayOrDash(entry.PppNumber)", markup, StringComparison.Ordinal);
        Assert.Contains("DisplayOrDash(entry.SerialNumber)", markup, StringComparison.Ordinal);
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
