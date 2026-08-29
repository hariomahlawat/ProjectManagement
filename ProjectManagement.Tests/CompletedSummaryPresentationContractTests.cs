using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class CompletedSummaryPresentationContractTests
{
    [Fact]
    public void Register_IsDefaultAndLegacyPortfolioCardGridIsRemoved()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-completed-summary.js");

        Assert.Contains("data-default-view=\"register\"", view, StringComparison.Ordinal);
        Assert.Contains("data-view-panel=\"register\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("cpw-portfolio-grid", view, StringComparison.Ordinal);
        Assert.Contains("completedProjectsWorkspaceViewV2", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HeadlineKpis_UseAvailabilityAndIndependentActionMeasures()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var policy = ReadRepoFile("Services", "Projects", "CompletedProjectPortfolioPolicy.cs");

        var summaryStart = view.IndexOf("<section class=\"cpw-summary-strip\"", StringComparison.Ordinal);
        var summaryEnd = view.IndexOf("<div class=\"cpw-workspace-toolbar\"", summaryStart, StringComparison.Ordinal);
        Assert.True(summaryStart >= 0 && summaryEnd > summaryStart);

        var summary = view[summaryStart..summaryEnd];
        Assert.Contains("Available for proliferation", summary, StringComparison.Ordinal);
        Assert.Contains("Proliferation assessment pending", summary, StringComparison.Ordinal);
        Assert.Contains("Technology review required", summary, StringComparison.Ordinal);
        Assert.Contains("ToT action pending", summary, StringComparison.Ordinal);
        Assert.Contains("Records with critical gaps", summary, StringComparison.Ordinal);
        Assert.Equal(5, CountOccurrences(summary, "class=\"cpw-summary-card"));
        Assert.DoesNotContain("Available but blocked", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Fully ready", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("available-blocked", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("fully-ready", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_UsesAvailabilityAndDistinctActionQueues()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");

        Assert.Contains("Availability posture", view, StringComparison.Ordinal);
        Assert.Contains("Recently completed projects available for proliferation", view, StringComparison.Ordinal);
        Assert.Contains("Proliferation decision not recorded", view, StringComparison.Ordinal);
        Assert.Contains("Technology review required", view, StringComparison.Ordinal);
        Assert.Contains("ToT action pending", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Available but blocked", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Fully ready for proliferation", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Drawer_IsReadOnlyContextWithStickyIdentityAndDedicatedEditNavigation()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var css = ReadRepoFile("wwwroot", "css", "pages", "projects-completed-summary.css");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-completed-summary.js");

        Assert.Contains("Completed project details", view, StringComparison.Ordinal);
        Assert.Contains("cpw-drawer-identity", view, StringComparison.Ordinal);
        Assert.Contains("Actions required", view, StringComparison.Ordinal);
        Assert.Contains("data-edit-project", view, StringComparison.Ordinal);
        Assert.Contains("Edit details", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Readiness", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("position: sticky", css, StringComparison.Ordinal);
        Assert.Contains("completedProjectsReturnStateV1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EditPage_UsesStructuredSectionsConditionalReasonAndCollapsedLppEntry()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Edit.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Edit.cshtml.cs");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-completed-summary-edit.js");

        Assert.Contains("Edit technology and proliferation details", view, StringComparison.Ordinal);
        Assert.Contains("Technology assessment", view, StringComparison.Ordinal);
        Assert.Contains("Availability for proliferation", view, StringComparison.Ordinal);
        Assert.Contains("Proliferation cost", view, StringComparison.Ordinal);
        Assert.Contains("Latest purchase price records", view, StringComparison.Ordinal);
        Assert.Contains("data-not-available-reason", view, StringComparison.Ordinal);
        Assert.Contains("data-new-lpp-panel", view, StringComparison.Ordinal);
        Assert.Contains("data-completed-project-edit-form", view, StringComparison.Ordinal);
        Assert.Contains("Url.IsLocalUrl", model, StringComparison.Ordinal);
        Assert.Contains("LocalRedirect", model, StringComparison.Ordinal);
        Assert.Contains("beforeunload", script, StringComparison.Ordinal);
        Assert.Contains("reasonInput.required", script, StringComparison.Ordinal);
        Assert.Contains("data-reason-clear-note", view, StringComparison.Ordinal);
        Assert.Contains("data-cancel-edit", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ProliferationCostTerminology_IsConsistentAcrossPresentationBoundaries()
    {
        var register = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var edit = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Edit.cshtml");
        var editModel = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Edit.cshtml.cs");
        var legacyMeta = ReadRepoFile("Pages", "Projects", "Meta", "Edit.cshtml");
        var legacyMetaModel = ReadRepoFile("Pages", "Projects", "Meta", "Edit.cshtml.cs");
        var policy = ReadRepoFile("Services", "Projects", "CompletedProjectPortfolioPolicy.cs");
        var summaryService = ReadRepoFile("Services", "Projects", "CompletedProjectsSummaryService.cs");
        var export = ReadRepoFile("Utilities", "Reporting", "CompletedProjectsSummaryExcelBuilder.cs");

        Assert.Contains("Proliferation cost", register, StringComparison.Ordinal);
        Assert.Contains("Proliferation cost (lakh)", edit, StringComparison.Ordinal);
        Assert.Contains("Proliferation cost remarks", edit, StringComparison.Ordinal);
        Assert.Contains("ProliferationCostLakhs", editModel, StringComparison.Ordinal);
        Assert.Contains("ProliferationCostRemarks", editModel, StringComparison.Ordinal);
        Assert.Contains("Proliferation cost (lakh)", legacyMeta, StringComparison.Ordinal);
        Assert.Contains("ProliferationCostLakhs", legacyMetaModel, StringComparison.Ordinal);
        Assert.Contains("\"Proliferation cost\"", policy, StringComparison.Ordinal);
        Assert.Contains("ProliferationCostLakhs => ApproxProductionCost", summaryService, StringComparison.Ordinal);
        Assert.Contains("\"Proliferation cost (lakh)\"", export, StringComparison.Ordinal);

        Assert.DoesNotContain("Approximate production cost", register, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Production information", edit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Production remarks", edit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approx Prod cost", legacyMeta, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Production cost\"", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("Approx. production cost", export, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_RetainsStickyHeaderAndWideScreenColumns()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml.cs");
        var css = ReadRepoFile("wwwroot", "css", "pages", "projects-completed-summary.css");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-completed-summary.js");

        Assert.Contains("cpw-secondary-column", view, StringComparison.Ordinal);
        Assert.Contains("cpw-wide-column", view, StringComparison.Ordinal);
        Assert.Contains("cpw-ultrawide-column", view, StringComparison.Ordinal);
        Assert.Contains("--cpw-sticky-offset", css, StringComparison.Ordinal);
        Assert.Contains("ResizeObserver", script, StringComparison.Ordinal);
        Assert.Contains("or \"category\" or \"build\"", model, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 1760px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 2160px)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalPresentationRefinements_PreserveMeaningAndContext()
    {
        var register = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var edit = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Edit.cshtml");
        var editModel = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Edit.cshtml.cs");
        var service = ReadRepoFile("Services", "Projects", "CompletedProjectsSummaryService.cs");
        var export = ReadRepoFile("Utilities", "Reporting", "CompletedProjectsSummaryExcelBuilder.cs");
        var css = ReadRepoFile("wwwroot", "css", "pages", "projects-completed-summary.css");
        var script = ReadRepoFile("wwwroot", "js", "pages", "projects-completed-summary.js");

        Assert.Contains("cpw-cell-proliferation-cost", register, StringComparison.Ordinal);
        Assert.Contains("Availability <span", register, StringComparison.Ordinal);
        Assert.Contains("<span>Availability</span>", register, StringComparison.Ordinal);
        Assert.Contains("cpw-remark-list", register, StringComparison.Ordinal);
        Assert.Contains("Availability for proliferation", register, StringComparison.Ordinal);
        Assert.Contains("Proliferation cost remarks", edit, StringComparison.Ordinal);
        Assert.Contains("Purchase history", edit, StringComparison.Ordinal);
        Assert.Contains("Completed project details updated.", editModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Completed-project details updated.", editModel, StringComparison.Ordinal);
        Assert.Contains("TechnologyRemarks", service, StringComparison.Ordinal);
        Assert.Contains("ProliferationRemarks", service, StringComparison.Ordinal);
        Assert.Contains("ProliferationCostRemarks", service, StringComparison.Ordinal);
        Assert.Contains("\"Availability for proliferation\"", export, StringComparison.Ordinal);
        Assert.Contains("white-space: normal", css, StringComparison.Ordinal);
        Assert.Contains("cpw-drawer-success", css, StringComparison.Ordinal);
        Assert.Contains("data-drawer-success", register, StringComparison.Ordinal);
        Assert.Contains("root.dataset.successMessage", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WideScreenColumns_AreBackedByServiceAndExport()
    {
        var service = ReadRepoFile("Services", "Projects", "CompletedProjectsSummaryService.cs");
        var export = ReadRepoFile("Utilities", "Reporting", "CompletedProjectsSummaryExcelBuilder.cs");

        Assert.Contains("Include(p => p.TechnicalCategory)", service, StringComparison.Ordinal);
        Assert.Contains("TechnicalCategoryName = p.TechnicalCategory?.Name", service, StringComparison.Ordinal);
        Assert.Contains("BuildType = p.IsBuild ? \"Rebuild\" : \"New\"", service, StringComparison.Ordinal);
        Assert.Contains("\"Technical category\"", export, StringComparison.Ordinal);
        Assert.Contains("\"Build type\"", export, StringComparison.Ordinal);
        Assert.Contains("private const int ColumnCount = 15", export, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_DefaultsToLatestCompletionAndExportsTheSameChronology()
    {
        var view = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml");
        var model = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Index.cshtml.cs");
        var service = ReadRepoFile("Services", "Projects", "CompletedProjectsSummaryService.cs");
        var overview = ReadRepoFile("Services", "Projects", "CompletedProjectsPortfolioOverview.cs");
        var export = ReadRepoFile("Utilities", "Reporting", "CompletedProjectsSummaryExcelBuilder.cs");

        Assert.Contains("Sort = \"completed\";", model, StringComparison.Ordinal);
        Assert.Contains("Dir = \"desc\";", model, StringComparison.Ordinal);
        Assert.Contains("GetSortAria(\"completed\")", view, StringComparison.Ordinal);
        Assert.Contains("GetRoutesForSort(\"completed\"", view, StringComparison.Ordinal);
        Assert.Contains("CompletedOn = p.CompletedOn", service, StringComparison.Ordinal);
        Assert.Contains("CompletedMonth = p.CompletedMonth", service, StringComparison.Ordinal);
        Assert.Contains("CompletedProjectCompletionOrdering.Apply(source, desc)", service, StringComparison.Ordinal);
        Assert.Contains("CompletedProjectCompletionOrdering", overview, StringComparison.Ordinal);
        Assert.Contains("\"Completed\"", export, StringComparison.Ordinal);
        Assert.Contains("item.FormatCompletion(string.Empty)", export, StringComparison.Ordinal);
    }


    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [Fact]
    public void ProliferationCostEditors_PreserveExplicitZeroAndRejectOnlyNegativeValues()
    {
        var completedView = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Edit.cshtml");
        var completedModel = ReadRepoFile("Pages", "Projects", "CompletedSummary", "Edit.cshtml.cs");
        var legacyMetaView = ReadRepoFile("Pages", "Projects", "Meta", "Edit.cshtml");
        var legacyMetaModel = ReadRepoFile("Pages", "Projects", "Meta", "Edit.cshtml.cs");

        Assert.Contains("min=\"0\"", completedView, StringComparison.Ordinal);
        Assert.DoesNotContain("min=\"0.01\"", completedView, StringComparison.Ordinal);
        Assert.Contains("Input.ProliferationCostLakhs is < 0m", completedModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Input.ProliferationCostLakhs is <= 0m", completedModel, StringComparison.Ordinal);

        Assert.Contains("asp-for=\"Input.ProliferationCostLakhs\" class=\"form-control\" type=\"number\" min=\"0\"", legacyMetaView, StringComparison.Ordinal);
        Assert.Contains("Input.ProliferationCostLakhs is < 0", legacyMetaModel, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}
