using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class FfcProjectsUpdatePresentationContractTests
{
    [Fact]
    public void Projects_reports_landing_page_exposes_ffc_projects_update()
    {
        var source = ReadRepoFile("Pages", "Projects", "Reports", "Index.cshtml");

        Assert.Contains("FFC Projects Update", source, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"./FfcProjectsUpdate\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Ffc_report_page_exposes_country_year_and_optional_overall_status_controls()
    {
        var source = ReadRepoFile("Pages", "Projects", "Reports", "FfcProjectsUpdate.cshtml");

        Assert.Contains("Country / Year", source, StringComparison.Ordinal);
        Assert.Contains("Overall status", source, StringComparison.Ordinal);
        Assert.Contains("data-ffc-country-year", source, StringComparison.Ordinal);
        Assert.Contains("data-default-included", source, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Word\"", source, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Pdf\"", source, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"Excel\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_country_year_rule_is_explicitly_all_installed_only()
    {
        var source = ReadRepoFile(
            "Services",
            "Reports",
            "FfcProjectsUpdate",
            "FfcProjectsUpdateContracts.cs");

        Assert.Contains(
            "string.Equals(row.Status, \"Installed\", StringComparison.OrdinalIgnoreCase)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("var defaultIncluded = !allInstalled;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Overall_status_uses_an_explicit_report_update_action()
    {
        var page = ReadRepoFile(
            "Pages",
            "Projects",
            "Reports",
            "FfcProjectsUpdate.cshtml");

        Assert.Contains("data-ffc-overall-status", page, StringComparison.Ordinal);
        Assert.Contains("data-ffc-refresh", page, StringComparison.Ordinal);
        Assert.Contains("Update report", page, StringComparison.Ordinal);
        Assert.DoesNotContain("onchange=\"this.form.submit()\"", page, StringComparison.Ordinal);

        var script = ReadRepoFile(
            "wwwroot",
            "js",
            "pages",
            "projects-reports-ffc.js");

        Assert.Contains("form.addEventListener(\"submit\"", script, StringComparison.Ordinal);
        Assert.Contains("syncHiddenSelection();", script, StringComparison.Ordinal);
        Assert.DoesNotContain("form.submit();", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Formal_exports_do_not_render_three_letter_country_codes()
    {
        var word = ReadRepoFile(
            "Services",
            "Reports",
            "FfcProjectsUpdate",
            "FfcProjectsUpdateWordBuilder.cs");
        var pdf = ReadRepoFile(
            "Services",
            "Reports",
            "FfcProjectsUpdate",
            "FfcProjectsUpdatePdfBuilder.cs");
        var excel = ReadRepoFile(
            "Services",
            "Reports",
            "FfcProjectsUpdate",
            "FfcProjectsUpdateExcelBuilder.cs");

        Assert.DoesNotContain("group.CountryCode", word, StringComparison.Ordinal);
        Assert.DoesNotContain("group.CountryCode", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("group.CountryCode", excel, StringComparison.Ordinal);
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
