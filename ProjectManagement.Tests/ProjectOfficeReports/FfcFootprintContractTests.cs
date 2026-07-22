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
    public void Footprint_ProvidesMapCardsPowerPointExportAndAccessibleCountryPanel()
    {
        var page = Read("Footprint.cshtml");
        var cards = Read("Partials/_FootprintCountryCards.cshtml");
        var panel = Read("Partials/_FootprintCountryPanel.cshtml");

        Assert.Contains("data-view=\"@Model.ViewMode\"", page, StringComparison.Ordinal);
        Assert.Contains("world-countries-simplified.geojson", page, StringComparison.Ordinal);
        Assert.Contains("Download PowerPoint", page, StringComparison.Ordinal);
        Assert.Contains("_PowerPointExportDrawer.cshtml", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Presentation mode", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("South Asia", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Africa", page, StringComparison.Ordinal);
        Assert.Contains("data-ffc-map-error", page, StringComparison.Ordinal);
        Assert.Contains("data-ffc-country-trigger", cards, StringComparison.Ordinal);
        Assert.Contains("offcanvas", panel, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"ffcCountryPanelTitle\"", panel, StringComparison.Ordinal);
        Assert.Contains("simulator quantity status", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<small>Qty</small>", cards, StringComparison.Ordinal);
        Assert.Contains("Total quantity", Read("ffc-footprint.js"), StringComparison.Ordinal);
        Assert.DoesNotContain("Total units", Read("ffc-footprint.js"), StringComparison.Ordinal);
        Assert.DoesNotContain("project position", page, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void PowerPointExport_UsesLocalStructuredGenerationAndRemovesBrowserPresentationMode()
    {
        var page = Read("Footprint.cshtml");
        var drawer = Read("Partials/_PowerPointExportDrawer.cshtml");
        var exportScript = Read("ffc-powerpoint-export.js");

        Assert.Contains("ExportPowerPoint", drawer, StringComparison.Ordinal);
        Assert.Contains("Executive brief", drawer, StringComparison.Ordinal);
        Assert.Contains("Full portfolio", drawer, StringComparison.Ordinal);
        Assert.Contains("Handling/classification marking", drawer, StringComparison.Ordinal);
        Assert.Contains("Generated locally from PRISM", drawer, StringComparison.Ordinal);
        Assert.Contains("fetch(form.action", exportScript, StringComparison.Ordinal);
        Assert.Contains("application/vnd.openxmlformats-officedocument.presentationml.presentation",
            Read("Presentation/FfcPowerPointExportService.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("presentation=true", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-ffc-focus", page, StringComparison.Ordinal);
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
