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
            "Cost (₹ Lakh)",
            "Quantity",
            "Status",
            "Progress / present status",
            "Overall remarks");

        Assert.Contains("ffc-dtable__group-row", markup, StringComparison.Ordinal);
        Assert.Contains("@group.CountryName – @group.Year", markup, StringComparison.Ordinal);
        Assert.Contains("rowspan=\"@rowSpan\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-kind=\"progress\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-kind=\"overall\"", markup, StringComparison.Ordinal);
        Assert.Contains("js-inline-save", markup, StringComparison.Ordinal);
        Assert.Contains("js-inline-cancel", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailedTableInlineEditor_PreservesExistingHandlerContract()
    {
        var script = ReadTestData("ffc-map-table-detailed-inline-edit.js");

        Assert.Contains("?handler=UpdateOverallRemarks", script, StringComparison.Ordinal);
        Assert.Contains("?handler=UpdateProgress", script, StringComparison.Ordinal);
        Assert.Contains("data-ffc-project-id", ReadTestData("_DetailedTablePartial.cshtml"), StringComparison.Ordinal);
        Assert.Contains("data-external-remark-id", ReadTestData("_DetailedTablePartial.cshtml"), StringComparison.Ordinal);
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
