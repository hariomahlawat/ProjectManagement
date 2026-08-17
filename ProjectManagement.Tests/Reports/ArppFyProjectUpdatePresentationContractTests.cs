using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class ArppFyProjectUpdatePresentationContractTests
{
    [Fact]
    public void Reports_pages_use_workspace_shell_and_detail_uses_authoritative_formal_title()
    {
        var index = ReadRepoFile("Pages", "Projects", "Reports", "Index.cshtml");
        var detail = ReadRepoFile("Pages", "Projects", "Reports", "ArppFyUpdate.cshtml");

        Assert.Contains("ViewData[\"UseFullWidth\"] = true", index, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"PageShell\"] = \"workspace\"", index, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"UseFullWidth\"] = true", detail, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"PageShell\"] = \"workspace\"", detail, StringComparison.Ordinal);
        Assert.Contains("@report.FormalTitle", detail, StringComparison.Ordinal);
        Assert.Contains("Report preflight", detail, StringComparison.Ordinal);
        Assert.Contains("Optional columns", detail, StringComparison.Ordinal);
        Assert.Contains("Present Stage", detail, StringComparison.Ordinal);
        Assert.Contains("asp-route-includePresentStage=\"@Model.IncludePresentStage\"", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Publication preflight", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ARPP Approved Projects – Project Update", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_css_keeps_workspace_uncapped_and_preflight_without_nested_vertical_scrolling()
    {
        var css = ReadRepoFile("wwwroot", "css", "pages", "projects-reports.css");

        Assert.Contains("width: 100%;", css, StringComparison.Ordinal);
        Assert.Contains("max-width: none;", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 1450px;", css, StringComparison.Ordinal);
        Assert.Contains("formal-report-table--with-stage", css, StringComparison.Ordinal);
        Assert.Contains("col-stage", css, StringComparison.Ordinal);
        Assert.Contains("--reports-sticky-top: 106px", css, StringComparison.Ordinal);
        Assert.DoesNotContain("max-width: 760px", css, StringComparison.Ordinal);
        Assert.DoesNotContain("max-height: 260px", css, StringComparison.Ordinal);
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
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}
