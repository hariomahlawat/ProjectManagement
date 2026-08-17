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
