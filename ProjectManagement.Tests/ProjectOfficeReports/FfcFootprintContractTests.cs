using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcFootprintContractTests
{
    [Fact]
    public void Overview_ExposesOneFootprintActionAndNoDuplicateCreateAction()
    {
        var overview = Read("Index.cshtml");

        Assert.Contains("/FFC/Footprint", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("World footprint", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("Country board", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("Create record", overview, StringComparison.Ordinal);
        Assert.Contains("Administration", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void Footprint_ProvidesMapCardsPresentationAndAccessibleCountryPanel()
    {
        var page = Read("Footprint.cshtml");
        var cards = Read("Partials/_FootprintCountryCards.cshtml");
        var panel = Read("Partials/_FootprintCountryPanel.cshtml");

        Assert.Contains("data-view=\"@Model.ViewMode\"", page, StringComparison.Ordinal);
        Assert.Contains("world-countries-simplified.geojson", page, StringComparison.Ordinal);
        Assert.Contains("Presentation", page, StringComparison.Ordinal);
        Assert.Contains("data-ffc-map-error", page, StringComparison.Ordinal);
        Assert.Contains("data-ffc-country-trigger", cards, StringComparison.Ordinal);
        Assert.Contains("offcanvas", panel, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"ffcCountryPanelTitle\"", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyPages_RedirectToUnifiedFootprint()
    {
        var mapModel = Read("Map.cshtml.cs");
        var boardModel = Read("MapBoard.cshtml.cs");

        Assert.Contains("/FFC/Footprint", mapModel, StringComparison.Ordinal);
        Assert.Contains("view = \"map\"", mapModel, StringComparison.Ordinal);
        Assert.Contains("/FFC/Footprint", boardModel, StringComparison.Ordinal);
        Assert.Contains("view = \"cards\"", boardModel, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailedTableProtectedAssets_RemainPresent()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "_DetailedTablePartial.cshtml")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "ffc-map-table-detailed-inline-edit.js")));
    }

    private static string Read(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"FFC footprint contract file was not copied to test output: {path}");
        return File.ReadAllText(path);
    }
}
