using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Reports.FfcProjectsUpdate;
using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class FfcProjectsUpdateExportTests
{
    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 7)]
    public void Excel_export_respects_optional_overall_status_column(
        bool includeOverallStatus,
        int expectedColumns)
    {
        var report = SampleReport();
        var bytes = FfcProjectsUpdateExcelBuilder.Build(
            report,
            new FfcProjectsUpdatePresentationOptions(includeOverallStatus));

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("FFC Projects Update");

        Assert.Equal(FfcProjectsUpdateReport.FormalTitle, worksheet.Cell(1, 1).GetString());
        Assert.Equal("Current progress", worksheet.Cell(3, 6).GetString());

        var countryGroupCell = worksheet.CellsUsed()
            .First(cell => cell.GetString().StartsWith("Ethiopia", StringComparison.Ordinal));
        Assert.Equal("Ethiopia – 2025", countryGroupCell.GetString());
        Assert.DoesNotContain("ETH", countryGroupCell.GetString(), StringComparison.Ordinal);
        Assert.Equal(
            XLAlignmentHorizontalValues.Left,
            countryGroupCell.Style.Alignment.Horizontal);

        if (includeOverallStatus)
        {
            Assert.Equal("Overall status", worksheet.Cell(3, 7).GetString());
        }
        else
        {
            Assert.True(string.IsNullOrEmpty(worksheet.Cell(3, 7).GetString()));
        }

        Assert.Equal(expectedColumns, worksheet.RangeUsed()!.ColumnCount());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Word_export_is_valid_and_contains_selected_country_year(bool includeOverallStatus)
    {
        var bytes = FfcProjectsUpdateWordBuilder.Build(
            SampleReport(),
            new FfcProjectsUpdatePresentationOptions(includeOverallStatus));

        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);

        var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
        Assert.Contains(FfcProjectsUpdateReport.FormalTitle, text, StringComparison.Ordinal);
        Assert.Contains("Ethiopia", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ETH", text, StringComparison.Ordinal);
        Assert.Contains("Current progress", text, StringComparison.Ordinal);

        var table = document.MainDocumentPart!.Document.Body!
            .Elements<W.Table>()
            .First();
        var headerCells = table.Elements<W.TableRow>()
            .First()
            .Elements<W.TableCell>()
            .ToArray();

        Assert.NotNull(headerCells[0].TableCellProperties?.GetFirstChild<W.NoWrap>());
        Assert.NotNull(headerCells[2].TableCellProperties?.GetFirstChild<W.NoWrap>());
        Assert.NotNull(headerCells[3].TableCellProperties?.GetFirstChild<W.NoWrap>());
        Assert.NotNull(headerCells[4].TableCellProperties?.GetFirstChild<W.NoWrap>());

        if (includeOverallStatus)
        {
            Assert.Contains("Overall status", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Pdf_export_generates_a_pdf_with_both_presentation_modes()
    {
        foreach (var includeOverallStatus in new[] { false, true })
        {
            var bytes = FfcProjectsUpdatePdfBuilder.Build(
                SampleReport(),
                new FfcProjectsUpdatePresentationOptions(includeOverallStatus));

            Assert.True(bytes.Length > 1000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
    }

    private static FfcProjectsUpdateReport SampleReport()
    {
        var rows = new[]
        {
            new FfcDetailedRowVm(
                1,
                1,
                "05 x MUUGV (Ethiopia 2025)",
                101,
                1.25m,
                5,
                "Planned",
                "TEC under progress.",
                "TEC under progress.",
                null,
                default,
                false),
            new FfcDetailedRowVm(
                2,
                2,
                "4 Lane IWTS Sml for FFC Ethiopia",
                102,
                0.70m,
                2,
                "Installed",
                "Installed at user location.",
                "Installed at user location.",
                null,
                default,
                false)
        };

        var group = new FfcDetailedGroupVm(
            55,
            "Ethiopia",
            "ETH",
            2025,
            "IPA received. Project demands under execution.",
            "IPA received. Project demands under execution.",
            rows,
            HasIncomplete: true);

        return FfcProjectsUpdateReportFactory.Create(
            new[] { group },
            FfcCountryYearSelectionMode.DefaultActive,
            null,
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
    }
}
