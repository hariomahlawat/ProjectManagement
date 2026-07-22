using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Ffc.Exports;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcDetailedTableExportTests
{
    private const string LongProgress =
        "Technical evaluation completed. Commercial evaluation is in progress and the complete project position must remain editable in the exported report. Tail marker: PROGRESS-END.";

    private const string LongOverallStatus =
        "IPA received and the case has been progressed through the concerned headquarters. Additional coordination is continuing for issue of equipment and delivery scheduling. Tail marker: OVERALL-END.";

    [Fact]
    public void WordBuilder_CreatesEditableLandscapeReportWithRepeatingHeaderAndPageFields()
    {
        var bytes = new FfcDetailedWordDocumentBuilder().Build(BuildContext("OFFICIAL"));

        Assert.True(bytes.Length > 4_000);
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var mainPart = Assert.IsType<MainDocumentPart>(document.MainDocumentPart);
        var body = Assert.IsType<Body>(mainPart.Document.Body);
        var documentText = body.InnerText;

        Assert.Contains("FFC Projects Update", documentText, StringComparison.Ordinal);
        Assert.Contains("Portfolio summary", documentText, StringComparison.Ordinal);
        Assert.Contains("Current progress", documentText, StringComparison.Ordinal);
        Assert.Contains("Overall status", documentText, StringComparison.Ordinal);
        Assert.Contains("PROGRESS-END", documentText, StringComparison.Ordinal);
        Assert.Contains("OVERALL-END", documentText, StringComparison.Ordinal);

        var section = Assert.Single(body.Elements<SectionProperties>());
        var pageSize = Assert.IsType<PageSize>(section.GetFirstChild<PageSize>());
        Assert.Equal(PageOrientationValues.Landscape, pageSize.Orient?.Value);
        Assert.Equal(16838U, pageSize.Width?.Value);
        Assert.Equal(11906U, pageSize.Height?.Value);

        var detailedTable = body.Elements<Table>().Last();
        var rows = detailedTable.Elements<TableRow>().ToArray();
        Assert.NotEmpty(rows);
        Assert.NotNull(rows[0].TableRowProperties?.GetFirstChild<TableHeader>());

        Assert.Contains(rows, row => row.Elements<TableCell>()
            .Any(cell => cell.TableCellProperties?.GridSpan?.Val?.Value == 7));
        Assert.Contains(detailedTable.Descendants<VerticalMerge>(), merge => merge.Val?.Value == MergedCellValues.Restart);
        Assert.Contains(detailedTable.Descendants<VerticalMerge>(), merge => merge.Val?.Value == MergedCellValues.Continue);

        var header = Assert.Single(mainPart.HeaderParts);
        Assert.Contains("OFFICIAL", header.Header?.InnerText ?? string.Empty, StringComparison.Ordinal);

        var footer = Assert.Single(mainPart.FooterParts);
        var footerXml = footer.Footer?.OuterXml ?? string.Empty;
        Assert.Contains("PRISM ERP", footer.Footer?.InnerText ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("PAGE", footerXml, StringComparison.Ordinal);
        Assert.Contains("NUMPAGES", footerXml, StringComparison.Ordinal);
    }

    [Fact]
    public void WordBuilder_LeavesHeaderMarkingAbsentWhenNotSupplied()
    {
        var bytes = new FfcDetailedWordDocumentBuilder().Build(BuildContext(null));

        using var stream = new MemoryStream(bytes, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var mainPart = Assert.IsType<MainDocumentPart>(document.MainDocumentPart);

        Assert.Empty(mainPart.HeaderParts);
        var footer = Assert.Single(mainPart.FooterParts);
        Assert.DoesNotContain("OFFICIAL", footer.Footer?.InnerText ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcelBuilder_CreatesFilterableLandscapeRegisterWithFullNarrativeAndNumericCells()
    {
        var bytes = new FfcDetailedExcelWorkbookBuilder().Build(BuildContext("OFFICIAL"));

        using var stream = new MemoryStream(bytes, writable: false);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("FFC Projects Update");

        Assert.Equal("FFC Projects Update", worksheet.Cell("A1").GetString());
        Assert.Equal("Country", worksheet.Cell(9, 1).GetString());
        Assert.Equal("Current progress", worksheet.Cell(9, 9).GetString());
        Assert.Equal("Overall status", worksheet.Cell(9, 10).GetString());
        Assert.Contains("PROGRESS-END", worksheet.Cell(10, 9).GetString(), StringComparison.Ordinal);
        Assert.Contains("OVERALL-END", worksheet.Cell(10, 10).GetString(), StringComparison.Ordinal);
        Assert.Equal(70m, worksheet.Cell(10, 6).GetValue<decimal>());
        Assert.Equal(2, worksheet.Cell(10, 7).GetValue<int>());
        Assert.True(worksheet.AutoFilter.IsEnabled);
        Assert.Equal(XLPageOrientation.Landscape, worksheet.PageSetup.PageOrientation);
        Assert.Equal(XLPaperSize.A4Paper, worksheet.PageSetup.PaperSize);

        using var packageStream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        var sheetEntry = archive.Entries.Single(entry => entry.FullName == "xl/worksheets/sheet1.xml");
        using var reader = new StreamReader(sheetEntry.Open());
        var sheetXml = reader.ReadToEnd();
        Assert.Contains("autoFilter", sheetXml, StringComparison.Ordinal);
        Assert.Contains("state=\"frozen\"", sheetXml, StringComparison.Ordinal);
        Assert.Contains("orientation=\"landscape\"", sheetXml, StringComparison.Ordinal);
    }

    private static FfcDetailedTableExportContext BuildContext(string? handlingMarking)
    {
        var rows = new[]
        {
            new FfcDetailedRowVm(
                FfcProjectId: 101,
                Serial: 1,
                ProjectName: "4 Lane IWTS Simulator",
                LinkedProjectId: 501,
                CostInCr: .70m,
                Quantity: 2,
                Status: "Delivered (not installed)",
                ProgressText: LongProgress,
                ProgressTextRaw: LongProgress,
                ExternalRemarkId: 12,
                ProgressSource: FfcProgressSource.ExternalProjectRemark,
                IsProgressEditable: true),
            new FfcDetailedRowVm(
                FfcProjectId: 102,
                Serial: 2,
                ProjectName: "VR CMC",
                LinkedProjectId: null,
                CostInCr: null,
                Quantity: 4,
                Status: "Planned",
                ProgressText: "Development in progress.",
                ProgressTextRaw: "Development in progress.",
                ExternalRemarkId: null,
                ProgressSource: FfcProgressSource.FfcProjectRemark,
                IsProgressEditable: false)
        };

        var groups = new[]
        {
            new FfcDetailedGroupVm(
                FfcRecordId: 9,
                CountryName: "Ethiopia",
                CountryCode: "ETH",
                Year: 2025,
                OverallRemarks: LongOverallStatus,
                OverallRemarksDisplay: LongOverallStatus,
                Rows: rows,
                HasIncomplete: true)
        };

        return new FfcDetailedTableExportContext(
            ScopeLabel: "Ethiopia · 2025",
            GeneratedAtUtc: new DateTimeOffset(2026, 7, 20, 4, 30, 0, TimeSpan.Zero),
            HandlingMarking: handlingMarking,
            Groups: groups);
    }
}
