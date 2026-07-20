using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class DashboardFfcWidgetContractTests
{
    [Fact]
    public void DashboardFfcWidget_UsesQualifiedMetricsAndActionableCountryLinks()
    {
        var markup = ReadRepoFile("Pages", "Dashboard", "Index.cshtml");

        Assert.Contains("Partner countries", markup, StringComparison.Ordinal);
        Assert.Contains("Completed breakdown", markup, StringComparison.Ordinal);
        Assert.Contains("delivered, awaiting installation", markup, StringComparison.Ordinal);
        Assert.Contains("Top countries by completed units", markup, StringComparison.Ordinal);
        Assert.Contains("asp-route-countryId=\"@country.CountryId\"", markup, StringComparison.Ordinal);
        Assert.Contains("DetailsUrl = Url.Page", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Countries covered", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardFfcWidget_EnablesCompactAutoFitAndMarkerDeconfliction()
    {
        var markup = ReadRepoFile("Pages", "Dashboard", "Index.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "widgets", "ffc-simulator-map.js");
        var css = ReadRepoFile("wwwroot", "css", "pages", "dashboard.css");

        Assert.Contains("data-auto-fit=\"true\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-deconflict-markers=\"true\"", markup, StringComparison.Ordinal);
        Assert.Contains("Amber badge: additional planned units", markup, StringComparison.Ordinal);
        Assert.Contains("buildCandidateOffsets", script, StringComparison.Ordinal);
        Assert.Contains("featureLabelLatLng", script, StringComparison.Ordinal);
        Assert.Contains("properties.ADM0_A3", script, StringComparison.Ordinal);
        Assert.Contains("ffc-simulator-map__leader", script, StringComparison.Ordinal);
        Assert.Contains("window.location.assign", script, StringComparison.Ordinal);
        Assert.Contains("min-height:266px", css, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}
