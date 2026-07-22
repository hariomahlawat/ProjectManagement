using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcPortfolioOverviewContractTests
{
    [Fact]
    public void Overview_UsesPortfolioRowsAndDoesNotRenderLegacyRecordCards()
    {
        var overview = ReadTestData("Index.cshtml");

        Assert.Contains("_PortfolioSummary.cshtml", overview, StringComparison.Ordinal);
        Assert.Contains("_PortfolioFilters.cshtml", overview, StringComparison.Ordinal);
        Assert.Contains("_PortfolioRow.cshtml", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("_FfcRecordCard", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("ffcFiltersModal", overview, StringComparison.Ordinal);
        Assert.Contains("/FFC/MapTableDetailed", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterContract_PreservesExistingQueryStringNamesAndRemovableChips()
    {
        var filters = ReadTestData("_PortfolioFilters.cshtml");

        Assert.Contains("name=\"q\"", filters, StringComparison.Ordinal);
        Assert.Contains("name=\"year\"", filters, StringComparison.Ordinal);
        Assert.Contains("name=\"countryId\"", filters, StringComparison.Ordinal);
        Assert.Contains("name=\"ipa\"", filters, StringComparison.Ordinal);
        Assert.Contains("name=\"gsl\"", filters, StringComparison.Ordinal);
        Assert.Contains("name=\"delivery\"", filters, StringComparison.Ordinal);
        Assert.Contains("name=\"installation\"", filters, StringComparison.Ordinal);
        Assert.Contains("remove: \"delivery\"", filters, StringComparison.Ordinal);
        Assert.Contains("data-ffc-advanced-filters", filters, StringComparison.Ordinal);
    }

    [Fact]
    public void PortfolioRow_UsesAccessibleNativeExpansionAndProjectRollup()
    {
        var row = ReadTestData("_PortfolioRow.cshtml");

        Assert.Contains("<details", row, StringComparison.Ordinal);
        Assert.Contains("<summary", row, StringComparison.Ordinal);
        Assert.Contains("data-ffc-portfolio-row", row, StringComparison.Ordinal);
        Assert.Contains("Project roll-up", row, StringComparison.Ordinal);
        Assert.Contains("/Projects/Overview", row, StringComparison.Ordinal);
        Assert.Contains("Current progress", row, StringComparison.Ordinal);
        Assert.Contains("Qty @Model.TotalUnits", row, StringComparison.Ordinal);
        Assert.Contains("Current status", row, StringComparison.Ordinal);
        Assert.Contains("role=\"columnheader\">Status", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Current position", row, StringComparison.Ordinal);
    }

    [Fact]
    public void PortfolioAssets_AreStrictlyScopedAndDoNotTargetDetailedTable()
    {
        var css = ReadTestData("ffc-portfolio.css");
        var script = ReadTestData("ffc-portfolio.js");

        Assert.Contains(".ffc-portfolio", css, StringComparison.Ordinal);
        Assert.DoesNotContain("ffc-dtable", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("map-table-detailed", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-ffc-portfolio-row", script, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateProgress", script, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateOverallRemarks", script, StringComparison.Ordinal);
    }

    private static string ReadTestData(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", fileName);
        Assert.True(File.Exists(path), $"FFC portfolio contract file was not copied to the test output: {path}");
        return File.ReadAllText(path);
    }
}
