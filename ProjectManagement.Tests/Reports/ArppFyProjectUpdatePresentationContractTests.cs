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
        Assert.Contains("Listing date", detail, StringComparison.Ordinal);
        Assert.Contains("Initial listing", detail, StringComparison.Ordinal);
        Assert.Contains("Current FY listing", detail, StringComparison.Ordinal);
        Assert.Contains("presentationOptions.ResolveListingDate(row)", detail, StringComparison.Ordinal);
        Assert.Contains("asp-route-includePresentStage=\"@Model.IncludePresentStage\"", detail, StringComparison.Ordinal);
        Assert.Contains("asp-route-listingDateMode=\"@presentationOptions.EffectiveListingDateMode\"", detail, StringComparison.Ordinal);
        Assert.Contains("formal-report-arpp", detail, StringComparison.Ordinal);
        Assert.Contains("/<wbr>", detail, StringComparison.Ordinal);
        Assert.Contains("@Pdc(row)", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("text-center text-nowrap\">@(row.PppNumber", detail, StringComparison.Ordinal);
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
        Assert.Contains("min-width: 1540px;", css, StringComparison.Ordinal);
        Assert.Contains("formal-report-table--with-stage", css, StringComparison.Ordinal);
        Assert.Contains("col-stage", css, StringComparison.Ordinal);
        Assert.Contains("col.col-arpp { width: 8.8rem; }", css, StringComparison.Ordinal);
        Assert.Contains("formal-report-arpp", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", css, StringComparison.Ordinal);
        Assert.Contains(".report-toolbar__listing", css, StringComparison.Ordinal);
        Assert.Contains("--reports-sticky-top: 106px", css, StringComparison.Ordinal);
        Assert.DoesNotContain("max-width: 760px", css, StringComparison.Ordinal);
        Assert.DoesNotContain("max-height: 260px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Listing_date_contract_maps_current_fy_source_date_and_all_exporters_use_the_selected_mode()
    {
        var service = ReadRepoFile(
            "Services",
            "Reports",
            "ArppFyProjectUpdate",
            "ArppFyProjectUpdateService.cs");
        Assert.Contains("arpp.SourceIssueDate", service, StringComparison.Ordinal);
        Assert.Contains("CurrentFyArppListingDate = row.CurrentFyArppListingDate", service, StringComparison.Ordinal);

        foreach (var builder in new[]
                 {
                     "ArppFyProjectUpdateWordBuilder.cs",
                     "ArppFyProjectUpdatePdfBuilder.cs",
                     "ArppFyProjectUpdateExcelBuilder.cs"
                 })
        {
            var source = ReadRepoFile(
                "Services",
                "Reports",
                "ArppFyProjectUpdate",
                builder);
            Assert.Contains("resolvedOptions.ResolveListingDate(row)", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Aon_milestone_contract_requires_completed_stage_and_does_not_warn_at_current_Aon()
    {
        var formalFacts = ReadRepoFile(
            "Services",
            "Projects",
            "ProjectFormalUpdateFactsResolver.cs");
        Assert.Contains("stage.Status == StageStatus.Completed", formalFacts, StringComparison.Ordinal);
        Assert.Contains("stage.CompletedOn.HasValue", formalFacts, StringComparison.Ordinal);

        var reportService = ReadRepoFile(
            "Services",
            "Reports",
            "ArppFyProjectUpdate",
            "ArppFyProjectUpdateService.cs");
        Assert.Contains(
            "row.StageOrder < ProjectStageMaturityOrder.AcceptanceOfNecessity",
            reportService,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "row.StageOrder <= ProjectStageMaturityOrder.AcceptanceOfNecessity",
            reportService,
            StringComparison.Ordinal);

        var ongoing = ReadRepoFile(
            "Services",
            "Projects",
            "OngoingProjectsReadService.cs");
        Assert.Contains(
            "stage?.Status == StageStatus.Completed",
            ongoing,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Word_builder_resolves_listing_date_from_the_options_in_scope()
    {
        var word = ReadRepoFile(
            "Services",
            "Reports",
            "ArppFyProjectUpdate",
            "ArppFyProjectUpdateWordBuilder.cs");

        Assert.Contains("options.ResolveListingDate(row)", word, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Cell(Date(resolvedOptions.ResolveListingDate(row))",
            word,
            StringComparison.Ordinal);
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
