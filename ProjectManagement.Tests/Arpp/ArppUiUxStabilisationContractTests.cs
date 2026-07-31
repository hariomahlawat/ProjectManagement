using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppUiUxStabilisationContractTests
{
    [Fact]
    public void Workspace_SeparatesDirtyValidationAndFilteringState()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("data-arpp-has-server-errors", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-save-state", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-filter-count=\"issues\"", markup, StringComparison.Ordinal);
        Assert.Contains("baselineSnapshot = createFormSnapshot()", script, StringComparison.Ordinal);
        Assert.Contains("else state.textContent = \"No unsaved changes\";", script, StringComparison.Ordinal);
        Assert.DoesNotContain("No unsaved changes ·", script, StringComparison.Ordinal);
        Assert.Contains("activeRowFilter", script, StringComparison.Ordinal);
        Assert.Contains("applyRowFilter", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if (initialIssues.length || hasServerValidationErrors) markDirty()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_UsesOneControlledGridScrollerAndProfessionalExitFlow()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("data-arpp-discard-return", markup, StringComparison.Ordinal);
        Assert.Contains("Discard and return", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-table-wrap", markup, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: auto auto minmax(0, 1fr);", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-workspace-v2__table-wrap", css, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior: contain", css, StringComparison.Ordinal);
        Assert.Contains("Discard unsaved changes?", script, StringComparison.Ordinal);
        Assert.Contains("tableWrap.scrollTo", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordAndRegister_PresentWorkingAndPublishedStatesClearly()
    {
        var record = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Details.cshtml");
        var register = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml");

        Assert.Contains("Published revision @Model.Issue.PublishedRevisionNumber remains visible", record, StringComparison.Ordinal);
        Assert.Contains("@valueStateLabel approved value", record, StringComparison.Ordinal);
        Assert.Contains("@valueStateLabel delisted value", record, StringComparison.Ordinal);
        Assert.Contains("Active published records", register, StringComparison.Ordinal);
        Assert.Contains("Records under work", register, StringComparison.Ordinal);
        Assert.Contains("Verified and published", register, StringComparison.Ordinal);
        Assert.Contains("Unlocked for correction", register, StringComparison.Ordinal);
        Assert.Contains("Ready for verification", register, StringComparison.Ordinal);
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
