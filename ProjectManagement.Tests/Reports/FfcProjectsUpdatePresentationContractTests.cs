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

    [Fact]
    public void Pdf_overall_status_is_a_country_year_row_span()
    {
        var source = ReadRepoFile(
            "Services",
            "Reports",
            "FfcProjectsUpdate",
            "FfcProjectsUpdatePdfBuilder.cs");

        Assert.Contains(
            "table.Cell().RowSpan((uint)group.Rows.Count)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("OverallStatusCell(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "index == 0 ? Narrative(group.OverallRemarks) : string.Empty",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ffc_long_register_keeps_the_table_header_sticky_not_the_kpi_strip()
    {
        var page = ReadRepoFile(
            "Pages",
            "Projects",
            "Reports",
            "FfcProjectsUpdate.cshtml");
        Assert.Contains("project-reports-page--ffc", page, StringComparison.Ordinal);

        var css = ReadRepoFile(
            "wwwroot",
            "css",
            "pages",
            "projects-reports.css");

        Assert.Contains(
            ".project-reports-page--ffc .report-command-strip",
            css,
            StringComparison.Ordinal);
        Assert.Contains(
            ".project-reports-page--ffc .ffc-projects-update-table thead th",
            css,
            StringComparison.Ordinal);
        Assert.Contains("position: sticky;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Ffc_sticky_table_header_is_flush_with_the_module_navigation()
    {
        var css = ReadRepoFile(
            "wwwroot",
            "css",
            "pages",
            "projects-reports.css");

        Assert.Contains(
            "--reports-nav-stack: 98px;",
            css,
            StringComparison.Ordinal);
        Assert.Contains(
            "--reports-sticky-top: calc(var(--reports-nav-stack) + 8px);",
            css,
            StringComparison.Ordinal);
        Assert.Contains(
            "--ffc-table-sticky-top: var(--reports-nav-stack);",
            css,
            StringComparison.Ordinal);
        Assert.Contains(
            "top: var(--ffc-table-sticky-top);",
            css,
            StringComparison.Ordinal);

        // Guard against reintroducing the 8px floating-card breathing room
        // into the FFC table header.
        Assert.DoesNotContain(
            ".ffc-projects-update-table thead th {\n        position: sticky;\n        top: var(--reports-sticky-top);",
            css,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Word_operational_headers_are_protected_from_avoidable_wrapping()
    {
        var source = ReadRepoFile(
            "Services",
            "Reports",
            "FfcProjectsUpdate",
            "FfcProjectsUpdateWordBuilder.cs");

        Assert.Contains("new W.NoWrap()", source, StringComparison.Ordinal);
        Assert.Contains(
            "noWrap: index is 0 or 2 or 3 or 4",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Word_uses_table_level_cell_margins_and_balanced_layouts()
    {
        var source = ReadRepoFile(
            "Services",
            "Reports",
            "FfcProjectsUpdate",
            "FfcProjectsUpdateWordBuilder.cs");

        Assert.Contains("private static W.TableCellMarginDefault DefaultCellMargins()", source, StringComparison.Ordinal);
        Assert.Contains("CellHorizontalMargin = 90", source, StringComparison.Ordinal);
        Assert.Contains("CellVerticalMargin = \"50\"", source, StringComparison.Ordinal);

        Assert.Contains(
            "[800, 3700, 1300, 1100, 1700, 7100]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[800, 3400, 1250, 1050, 1700, 3750, 3750]",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Word_declares_modern_compatibility_and_schema_clean_vertical_merges()
    {
        var source = ReadRepoFile(
            "Services",
            "Reports",
            "FfcProjectsUpdate",
            "FfcProjectsUpdateWordBuilder.cs");

        Assert.Contains(
            "Name = W.CompatSettingNameValues.CompatibilityMode",
            source,
            StringComparison.Ordinal);
        Assert.Contains("CompatibilityMode = \"15\"", source, StringComparison.Ordinal);
        Assert.Contains("verticalMerge: verticalMerge", source, StringComparison.Ordinal);
        Assert.Contains("properties.Append(new W.VerticalMerge", source, StringComparison.Ordinal);

        var mergeIndex = source.IndexOf(
            "properties.Append(new W.VerticalMerge",
            StringComparison.Ordinal);
        var shadingIndex = source.IndexOf(
            "properties.Append(new W.Shading",
            StringComparison.Ordinal);
        var noWrapIndex = source.IndexOf(
            "properties.Append(new W.NoWrap",
            StringComparison.Ordinal);
        var alignIndex = source.IndexOf(
            "properties.Append(new W.TableCellVerticalAlignment",
            StringComparison.Ordinal);

        Assert.True(mergeIndex >= 0);
        Assert.True(shadingIndex > mergeIndex);
        Assert.True(noWrapIndex > shadingIndex);
        Assert.True(alignIndex > noWrapIndex);
    }

    [Fact]
    public void Ffc_export_buttons_are_disabled_until_pending_options_are_applied()
    {
        var page = ReadRepoFile(
            "Pages",
            "Projects",
            "Reports",
            "FfcProjectsUpdate.cshtml");
        Assert.Contains("data-ffc-export", page, StringComparison.Ordinal);
        Assert.Contains("data-ffc-update-required", page, StringComparison.Ordinal);

        var script = ReadRepoFile(
            "wwwroot",
            "js",
            "pages",
            "projects-reports-ffc.js");

        Assert.Contains("const appliedState = Object.freeze", script, StringComparison.Ordinal);
        Assert.Contains("const hasPendingChanges", script, StringComparison.Ordinal);
        Assert.Contains("updatePendingState();", script, StringComparison.Ordinal);
        Assert.Contains("setExportDisabled(link, disabled)", script, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault();", script, StringComparison.Ordinal);
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
