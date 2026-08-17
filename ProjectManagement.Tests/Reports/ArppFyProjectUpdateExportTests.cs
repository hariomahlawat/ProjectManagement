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
    public void Export_builders_emit_the_single_authoritative_formal_heading()
    {
        var report = SampleReport();

        var word = new ArppFyProjectUpdateWordBuilder().Build(report);
        Assert.NotEmpty(word);
        using (var stream = new MemoryStream(word))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            Assert.NotNull(document.MainDocumentPart?.Document?.Body);
            var text = document.MainDocumentPart!.Document.Body!.InnerText;
            Assert.Contains(report.FormalTitle, text, StringComparison.Ordinal);
            Assert.DoesNotContain("ARPP APPROVED PROJECTS", text, StringComparison.Ordinal);
            Assert.Contains("Sample Simulator", text, StringComparison.Ordinal);
            var footerText = document.MainDocumentPart.FooterParts.Single().Footer?.InnerText ?? string.Empty;
            Assert.Contains("PRISM ERP · Simulator Development Division", footerText, StringComparison.Ordinal);
            Assert.Contains("Page 1 of 1", footerText, StringComparison.Ordinal);
        }

        var excel = new ArppFyProjectUpdateExcelBuilder().Build(report);
        Assert.NotEmpty(excel);
        using var excelStream = new MemoryStream(excel);
        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheet("ARPP Project Update");
        Assert.Equal(report.FormalTitle, worksheet.Cell(1, 1).GetString());
        Assert.True(string.IsNullOrWhiteSpace(worksheet.Cell(2, 1).GetString()));
        Assert.Equal("Status", worksheet.Cell(3, 8).GetString());
        Assert.Equal("SO amt & dt", worksheet.Cell(4, 9).GetString());
        Assert.Equal("Sample Simulator", worksheet.Cell(5, 3).GetString());

        var pdf = new ArppFyProjectUpdatePdfBuilder().Build(report);
        Assert.True(pdf.Length > 1000);
        Assert.Equal((byte)'%', pdf[0]);
    }


    [Fact]
    public void Formal_exports_leave_missing_report_values_blank()
    {
        var report = SampleReportWithMissingValues();

        var word = new ArppFyProjectUpdateWordBuilder().Build(report);
        using (var stream = new MemoryStream(word))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
            Assert.DoesNotContain("—", text, StringComparison.Ordinal);
        }

        var excel = new ArppFyProjectUpdateExcelBuilder().Build(report);
        using var excelStream = new MemoryStream(excel);
        using var workbook = new XLWorkbook(excelStream);
        var worksheet = workbook.Worksheet("ARPP Project Update");

        Assert.True(string.IsNullOrEmpty(worksheet.Cell(5, 2).GetString()));
        Assert.True(string.IsNullOrEmpty(worksheet.Cell(5, 4).GetString()));
        Assert.True(string.IsNullOrEmpty(worksheet.Cell(5, 5).GetString()));
        Assert.True(string.IsNullOrEmpty(worksheet.Cell(5, 6).GetString()));
        Assert.True(string.IsNullOrEmpty(worksheet.Cell(5, 8).GetString()));
        Assert.True(string.IsNullOrEmpty(worksheet.Cell(5, 9).GetString()));
        Assert.True(string.IsNullOrEmpty(worksheet.Cell(5, 10).GetString()));
        Assert.True(string.IsNullOrEmpty(worksheet.Cell(5, 12).GetString()));
    }

    [Fact]
    public void Pdf_builder_disables_standard_ligatures_for_text_integrity()
    {
        var source = ReadRepoFile(
            "Services",
            "Reports",
            "ArppFyProjectUpdate",
            "ArppFyProjectUpdatePdfBuilder.cs");

        Assert.Contains(
            ".DisableFontFeature(FontFeatures.StandardLigatures)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("?? \"—\"", source, StringComparison.Ordinal);
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

    private static ArppFyProjectUpdateReport SampleReportWithMissingValues()
        => new(
            2026,
            new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            new[]
            {
                new ArppFyProjectUpdateRow(
                    1,
                    10,
                    null,
                    "Sample Simulator",
                    null,
                    null,
                    null,
                    "SDD",
                    null,
                    null,
                    ProjectSupplyOrderValueBasis.None,
                    null,
                    null,
                    ArppCategory.CarryForward,
                    null,
                    null,
                    ProjectLifecycleStatus.Active,
                    false,
                    "AON",
                    "Acceptance of Necessity",
                    ProjectStageMaturityOrder.AcceptanceOfNecessity)
            },
            Array.Empty<ArppFyReportWarning>(),
            0);

    private static string ReadRepoFile(params string[] segments)
    {
        var root = ResolveRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()));
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }

}
