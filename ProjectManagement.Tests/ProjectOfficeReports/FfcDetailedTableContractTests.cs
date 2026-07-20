using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcDetailedTableContractTests
{
    [Fact]
    public void DetailedTablePartial_PreservesRequiredColumnOrderAndCountryGrouping()
    {
        var markup = ReadTestData("_DetailedTablePartial.cshtml");

        AssertAppearsInOrder(
            markup,
            "S. No.",
            "Project",
            "Cost (₹ lakh)",
            "Quantity",
            "Status",
            "Current progress",
            "Overall status");

        Assert.Contains("ffc-dtable__group-row", markup, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/FFC/Records/Details\"", markup, StringComparison.Ordinal);
        Assert.Contains("asp-route-id=\"@group.FfcRecordId\"", markup, StringComparison.Ordinal);
        Assert.Contains("asp-route-returnUrl=\"@returnUrl\"", markup, StringComparison.Ordinal);
        Assert.Contains("@group.CountryName – @group.Year", markup, StringComparison.Ordinal);
        Assert.Contains("rowspan=\"@rowSpan\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-kind=\"progress\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-kind=\"overall\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-expandable", markup, StringComparison.Ordinal);
        Assert.Contains("js-expand-status", markup, StringComparison.Ordinal);
        Assert.Contains("js-inline-save", markup, StringComparison.Ordinal);
        Assert.Contains("js-inline-cancel", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailedTablePage_ProvidesCompactWordAndExcelExportMenuWithoutPermanentFilters()
    {
        var markup = ReadTestData("MapTableDetailed.cshtml");

        Assert.Contains("FFC Projects Update", markup, StringComparison.Ordinal);
        Assert.Contains("Export to Word", markup, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"ExportWord\"", markup, StringComparison.Ordinal);
        Assert.Contains("Export to Excel", markup, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"ExportExcel\"", markup, StringComparison.Ordinal);
        Assert.Contains("Handling / classification marking", ReadTestData("MapTableDetailed.cshtml.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("form-select", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("permanent-filter", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetailedTablePresentation_ImplementsStickyHeaderExpansionAndLandscapePrint()
    {
        var screenStyles = ReadTestData("ffc-detailed-table.css");
        var printStyles = ReadTestData("ffc-detailed-table.print.css");
        var script = ReadTestData("ffc-map-table-detailed.js");

        Assert.Contains("position: sticky", screenStyles, StringComparison.Ordinal);
        Assert.Contains("--ffc-sticky-top: 98px", screenStyles, StringComparison.Ordinal);
        Assert.Contains("overflow-y: visible", screenStyles, StringComparison.Ordinal);
        Assert.Contains("ffc-dtable__clamp", screenStyles, StringComparison.Ordinal);
        Assert.Contains("A4 landscape", printStyles, StringComparison.Ordinal);
        Assert.Contains("max-height: none", printStyles, StringComparison.Ordinal);
        Assert.Contains("data-expandable", script, StringComparison.Ordinal);
        Assert.Contains("ffc:detailed-table-content-updated", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailedTableInlineEditor_PreservesExistingHandlerContract()
    {
        var script = ReadTestData("ffc-map-table-detailed-inline-edit.js");

        Assert.Contains("?handler=UpdateOverallRemarks", script, StringComparison.Ordinal);
        Assert.Contains("?handler=UpdateProgress", script, StringComparison.Ordinal);
        Assert.Contains("data-ffc-project-id", ReadTestData("_DetailedTablePartial.cshtml"), StringComparison.Ordinal);
        Assert.Contains("data-external-remark-id", ReadTestData("_DetailedTablePartial.cshtml"), StringComparison.Ordinal);
        Assert.Contains("ffc:detailed-table-content-updated", script, StringComparison.Ordinal);
    }

    private static string ReadTestData(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", fileName);
        Assert.True(File.Exists(path), $"Detailed-table contract file was not copied to the test output: {path}");
        return File.ReadAllText(path);
    }

    private static void AssertAppearsInOrder(string value, params string[] expected)
    {
        var previousIndex = -1;
        foreach (var item in expected)
        {
            var currentIndex = value.IndexOf(item, StringComparison.Ordinal);
            Assert.True(currentIndex >= 0, $"Expected '{item}' was not found.");
            Assert.True(currentIndex > previousIndex, $"Expected '{item}' after the previous column.");
            previousIndex = currentIndex;
        }
    }
}
