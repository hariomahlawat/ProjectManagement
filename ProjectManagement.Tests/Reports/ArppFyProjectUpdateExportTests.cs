using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Reports.ArppFyProjectUpdate;
using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class ArppFyProjectUpdateExportTests
{
    [Fact]
    public void Word_and_excel_builders_emit_valid_landscape_report_files()
    {
        var report = SampleReport();

        var word = new ArppFyProjectUpdateWordBuilder().Build(report);
        Assert.NotEmpty(word);
        using (var stream = new MemoryStream(word))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            Assert.NotNull(document.MainDocumentPart?.Document?.Body);
            var text = document.MainDocumentPart!.Document.Body!.InnerText;
            Assert.Contains("ARPP APPROVED PROJECTS", text, StringComparison.Ordinal);
            Assert.Contains("Sample Simulator", text, StringComparison.Ordinal);
        }

        var excel = new ArppFyProjectUpdateExcelBuilder().Build(report);
        Assert.NotEmpty(excel);
        using var excelStream = new MemoryStream(excel);
        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheet("ARPP Project Update");
        Assert.Equal("Status", worksheet.Cell(3, 8).GetString());
        Assert.Equal("SO amt & dt", worksheet.Cell(4, 9).GetString());
        Assert.Equal("Sample Simulator", worksheet.Cell(5, 3).GetString());
    }

    private static ArppFyProjectUpdateReport SampleReport()
        => new(
            2026,
            new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            new[]
            {
                new ArppFyProjectUpdateRow(
                    1,
                    10,
                    "33",
                    "Sample Simulator",
                    new DateOnly(2025, 4, 10),
                    "Sch 4",
                    "DG EME",
                    "SDD",
                    new DateOnly(2025, 6, 1),
                    23_500_000m,
                    ProjectSupplyOrderValueBasis.Pnc,
                    new DateOnly(2026, 7, 14),
                    new DateOnly(2027, 1, 31),
                    ArppCategory.CarryForward,
                    "Development progressing as planned.",
                    new DateOnly(2026, 8, 10),
                    ProjectLifecycleStatus.Active,
                    false,
                    "DEVP",
                    "Development",
                    ProjectStageMaturityOrder.Development)
            },
            Array.Empty<ArppFyReportWarning>(),
            0);
}
