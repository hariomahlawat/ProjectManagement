using Xunit;

namespace ProjectManagement.Tests.Arpp;

public sealed class ArppUiUxStabilisationContractTests
{
    [Fact]
    public void ManageWorkspace_SeparatesValidationFromUnsavedChanges()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("data-arpp-has-server-errors", markup, StringComparison.Ordinal);
        Assert.Contains("baselineSnapshot = createFormSnapshot()", script, StringComparison.Ordinal);
        Assert.Contains("No unsaved changes ·", script, StringComparison.Ordinal);
        Assert.Contains("serverRejectedChanges", script, StringComparison.Ordinal);
        Assert.DoesNotContain("if (initialIssues.length || hasServerValidationErrors) markDirty()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ManageWorkspace_UsesStableTableViewportAndProfessionalDiscardFlow()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Manage.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "project-office-reports", "arpp-workspace.css");
        var script = ReadRepoFile("wwwroot", "js", "pages", "project-office-reports", "arpp", "arpp-manage.js");

        Assert.Contains("data-arpp-discard-return", markup, StringComparison.Ordinal);
        Assert.Contains("Discard and return", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-validation-more", markup, StringComparison.Ordinal);
        Assert.Contains("role=\"status\" aria-live=\"polite\"", markup, StringComparison.Ordinal);
        Assert.Contains(".arpp-page--workspace .arpp-table-wrap", css, StringComparison.Ordinal);
        Assert.Contains("overflow: auto;", css, StringComparison.Ordinal);
        Assert.Contains(".arpp-entry-table thead th {\n    top: 0;", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--bs-body-bg);", css, StringComparison.Ordinal);
        Assert.DoesNotContain("window.addEventListener(\"scroll\", updateStickyMetrics", script, StringComparison.Ordinal);
        Assert.Contains("Discard unsaved changes?", script, StringComparison.Ordinal);
        Assert.Contains("validationExpanded", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailsPage_UsesOneStatePanelAndPlacesRowsBeforeAttachmentManagement()
    {
        var markup = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Details.cshtml");

        Assert.Contains("arpp-document-state", markup, StringComparison.Ordinal);
        Assert.Contains("Unlocked for correction", markup, StringComparison.Ordinal);
        Assert.Contains("Verify and publish", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("arpp-publication-continuity", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("arpp-verification-banner", markup, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(markup, "<article class=\"arpp-kpi"));
        Assert.True(
            markup.IndexOf("<h2>Issued rows</h2>", StringComparison.Ordinal) <
            markup.IndexOf("id=\"arpp-issued-document-heading\"", StringComparison.Ordinal));
        Assert.Contains("arpp-document-file--compact", markup, StringComparison.Ordinal);
        Assert.Contains("data-arpp-document-management", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterAndPublishedLibrary_UseClearWorkingAndSearchLanguage()
    {
        var register = ReadRepoFile("Areas", "ProjectOfficeReports", "Pages", "ARPP", "Index.cshtml");
        var library = ReadRepoFile("Pages", "Projects", "Arpp", "Index.cshtml");

        Assert.Contains("Verified and published", register, StringComparison.Ordinal);
        Assert.Contains("Verification pending", register, StringComparison.Ordinal);
        Assert.Contains("Working approved value", register, StringComparison.Ordinal);
        Assert.Contains("Working delisted value", register, StringComparison.Ordinal);
        Assert.Contains("Edit working copy", register, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Search ARPP records\"", library, StringComparison.Ordinal);
        Assert.Contains("bi bi-search", library, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
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
