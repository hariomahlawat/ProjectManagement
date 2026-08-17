using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class ProjectsReportsUxContractTests
{
    [Fact]
    public void Reports_catalogue_uses_two_columns_on_wide_screens_with_single_column_fallback()
    {
        var index = ReadRepoFile(
            "Pages",
            "Projects",
            "Reports",
            "Index.cshtml");
        var css = ReadRepoFile(
            "wwwroot",
            "css",
            "pages",
            "projects-reports.css");

        Assert.Contains("ARPP FY Project Update", index, StringComparison.Ordinal);
        Assert.Contains("FFC Projects Update", index, StringComparison.Ordinal);
        Assert.Contains(
            "grid-template-columns: repeat(2, minmax(0, 1fr));",
            css,
            StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1240px)", css, StringComparison.Ordinal);
        Assert.Contains(
            "grid-template-columns: minmax(0, 1fr);",
            css,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Arpp_report_uses_explicit_update_workflow_instead_of_auto_submit()
    {
        var source = ReadRepoFile(
            "Pages",
            "Projects",
            "Reports",
            "ArppFyUpdate.cshtml");

        Assert.Contains("data-report-controls", source, StringComparison.Ordinal);
        Assert.Contains("data-report-update", source, StringComparison.Ordinal);
        Assert.Contains("data-report-update-required", source, StringComparison.Ordinal);
        Assert.Equal(
            3,
            CountOccurrences(source, "data-report-setting-key="));
        Assert.Equal(
            3,
            CountOccurrences(source, "data-report-export"));

        Assert.DoesNotContain("onchange=\"this.form.submit()\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("title=\"Refresh report\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Arpp_and_ffc_share_the_same_pending_state_controller()
    {
        var arpp = ReadRepoFile(
            "Pages",
            "Projects",
            "Reports",
            "ArppFyUpdate.cshtml");
        var ffc = ReadRepoFile(
            "Pages",
            "Projects",
            "Reports",
            "FfcProjectsUpdate.cshtml");
        var controller = ReadRepoFile(
            "wwwroot",
            "js",
            "pages",
            "projects-report-controls.js");

        Assert.Contains("projects-report-controls.js", arpp, StringComparison.Ordinal);
        Assert.Contains("projects-report-controls.js", ffc, StringComparison.Ordinal);

        Assert.Contains("[data-report-controls]", controller, StringComparison.Ordinal);
        Assert.Contains("[data-report-setting]", controller, StringComparison.Ordinal);
        Assert.Contains("[data-report-update]", controller, StringComparison.Ordinal);
        Assert.Contains("[data-report-update-required]", controller, StringComparison.Ordinal);
        Assert.Contains("[data-report-export]", controller, StringComparison.Ordinal);
        Assert.Contains("prism:report-settings-changed", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Ffc_script_only_owns_country_year_specific_behaviour()
    {
        var source = ReadRepoFile(
            "wwwroot",
            "js",
            "pages",
            "projects-reports-ffc.js");

        Assert.Contains("syncHiddenSelection", source, StringComparison.Ordinal);
        Assert.Contains("data-ffc-country-action", source, StringComparison.Ordinal);
        Assert.Contains("prism:report-settings-changed", source, StringComparison.Ordinal);

        Assert.DoesNotContain("baseExportDisabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("setExportDisabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("report-refresh-button--pending", source, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;

        while (true)
        {
            var index = source.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            start = index + value.Length;
        }
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

        throw new DirectoryNotFoundException(
            "Could not locate the ProjectManagement repository root.");
    }
}
