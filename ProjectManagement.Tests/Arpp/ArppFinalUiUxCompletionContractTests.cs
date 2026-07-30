using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppFinalUiUxCompletionContractTests
{
    [Fact]
    public void Workspace_UsesOneVerticalScrollSurfaceAndCompactIssueTray()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("data-arpp-validation-toggle", markup, StringComparison.Ordinal);
        Assert.Contains("id=\"arppValidationTray\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-validation-close", markup, StringComparison.Ordinal);
        Assert.Contains("disabled aria-disabled=\"true\"", markup, StringComparison.Ordinal);
        Assert.Contains("max-height: none;", css, StringComparison.Ordinal);
        Assert.Contains("overflow-y: visible;", css, StringComparison.Ordinal);
        Assert.Contains("body.arpp-workspace-fullscreen [data-arpp-workspace]", css, StringComparison.Ordinal);
        Assert.Contains("setValidationTrayOpen", script, StringComparison.Ordinal);
        Assert.Contains("No changes to save", script, StringComparison.Ordinal);
        Assert.Contains("setSaveButtonsDisabled(saving || !dirty)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Details_UsesCompactProjectCellsAndStickyColumnHeadings()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Details.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp.css");

        Assert.Contains("<colgroup>", markup, StringComparison.Ordinal);
        Assert.Contains("arpp-details-col-project", markup, StringComparison.Ordinal);
        Assert.Contains("IsSameProjectReference", markup, StringComparison.Ordinal);
        Assert.Contains("arpp-details-project-link", markup, StringComparison.Ordinal);
        Assert.Contains("arpp-details-project-meta", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("> Edit rows</a>", markup, StringComparison.Ordinal);
        Assert.Contains("table-layout: fixed;", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-details-col-project { width: 39%; }", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-details-table thead th", css, StringComparison.Ordinal);
        Assert.Contains("top: 52px;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_UsesPreciseLifecycleStatesAndRecordLanguage()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml");
        var contracts = ReadRepoFile("Services", "Arpp", "ArppContracts.cs");
        var readService = ReadRepoFile("Services", "Arpp", "ArppReadService.cs");

        Assert.Contains("ARPP records", markup, StringComparison.Ordinal);
        Assert.Contains("published ·", markup, StringComparison.Ordinal);
        Assert.Contains("Unlocked for correction", markup, StringComparison.Ordinal);
        Assert.Contains("Setup incomplete", markup, StringComparison.Ordinal);
        Assert.Contains("Reference review required", markup, StringComparison.Ordinal);
        Assert.Contains("Ready for verification", markup, StringComparison.Ordinal);
        Assert.Contains("Revision @issue.PublishedRevisionNumber remains published", markup, StringComparison.Ordinal);
        Assert.Contains("HasPublishedSnapshot", contracts, StringComparison.Ordinal);
        Assert.Contains("HasUnresolvedReferenceData", contracts, StringComparison.Ordinal);
        Assert.Contains("issue.PublishedSnapshot != null", readService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var root = ResolveRepoRoot();
        var path = Path.Combine(new[] { root }.Concat(segments).ToArray());
        return File.ReadAllText(path);
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}
