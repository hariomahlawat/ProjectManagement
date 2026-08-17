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
    public void Present_stage_option_adds_the_stage_column_to_word_excel_and_pdf_contract()
    {
        var report = SampleReport();
        var options = new ArppFyProjectUpdatePresentationOptions(IncludePresentStage: true);

        var word = new ArppFyProjectUpdateWordBuilder().Build(report, options);
        using (var stream = new MemoryStream(word))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
            Assert.Contains("Present Stage", text, StringComparison.Ordinal);
            Assert.Contains("Development", text, StringComparison.Ordinal);
        }

        var excel = new ArppFyProjectUpdateExcelBuilder().Build(report, options);
        using (var excelStream = new MemoryStream(excel))
        using (var workbook = new XLWorkbook(excelStream))
        {
            var worksheet = workbook.Worksheet("ARPP Project Update");
            Assert.Equal("Present Stage", worksheet.Cell(4, 9).GetString());
            Assert.Equal("Development", worksheet.Cell(5, 9).GetString());
            Assert.Equal("SO amt & dt", worksheet.Cell(4, 10).GetString());
            Assert.Equal("PDC dt", worksheet.Cell(4, 11).GetString());
            Assert.Equal("Proj Case", worksheet.Cell(3, 12).GetString());
            Assert.Equal("Remarks", worksheet.Cell(3, 13).GetString());
            Assert.Equal(report.FormalTitle, worksheet.Cell(1, 1).GetString());
        }

        var pdf = new ArppFyProjectUpdatePdfBuilder().Build(report, options);
        Assert.True(pdf.Length > 1000);
        Assert.Equal((byte)'%', pdf[0]);
    }

    [Fact]
    public void Present_stage_option_is_off_by_default_and_completed_stage_display_is_authoritative()
    {
        var defaults = ArppFyProjectUpdatePresentationOptions.Default;
        Assert.False(defaults.IncludePresentStage);
        Assert.Equal(ArppListingDateMode.InitialListing, defaults.EffectiveListingDateMode);
        Assert.Equal(12, defaults.ColumnCount);
        Assert.Equal(3, defaults.StatusColumnCount);

        var completed = SampleReport().Rows[0] with
        {
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CurrentStageCode = "DEVP",
            CurrentStageLabel = "Development"
        };

        Assert.Equal("Completed", completed.StageDisplay);
    }

    [Fact]
    public void Listing_date_mode_switches_between_initial_and_current_fy_in_formal_exports()
    {
        var report = SampleReport();
        var row = report.Rows[0];

        var initialOptions = new ArppFyProjectUpdatePresentationOptions(
            IncludePresentStage: false,
            ListingDateMode: ArppListingDateMode.InitialListing);
        var currentFyOptions = new ArppFyProjectUpdatePresentationOptions(
            IncludePresentStage: false,
            ListingDateMode: ArppListingDateMode.CurrentFinancialYear);

        Assert.Equal(new DateOnly(2025, 4, 10), initialOptions.ResolveListingDate(row));
        Assert.Equal(new DateOnly(2026, 7, 27), currentFyOptions.ResolveListingDate(row));

        var initialWord = new ArppFyProjectUpdateWordBuilder().Build(report, initialOptions);
        using (var stream = new MemoryStream(initialWord))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
            Assert.Contains("10 Apr 2025", text, StringComparison.Ordinal);
            Assert.DoesNotContain("27 Jul 2026", text, StringComparison.Ordinal);
        }

        var currentFyWord = new ArppFyProjectUpdateWordBuilder().Build(report, currentFyOptions);
        using (var stream = new MemoryStream(currentFyWord))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
            Assert.Contains("27 Jul 2026", text, StringComparison.Ordinal);
            Assert.DoesNotContain("10 Apr 2025", text, StringComparison.Ordinal);
        }

        var initialExcel = new ArppFyProjectUpdateExcelBuilder().Build(report, initialOptions);
        using (var stream = new MemoryStream(initialExcel))
        using (var workbook = new XLWorkbook(stream))
        {
            Assert.Equal(new DateTime(2025, 4, 10), workbook.Worksheet("ARPP Project Update").Cell(5, 4).GetDateTime());
        }

        var currentFyExcel = new ArppFyProjectUpdateExcelBuilder().Build(report, currentFyOptions);
        using (var stream = new MemoryStream(currentFyExcel))
        using (var workbook = new XLWorkbook(stream))
        {
            Assert.Equal(new DateTime(2026, 7, 27), workbook.Worksheet("ARPP Project Update").Cell(5, 4).GetDateTime());
        }

        var pdf = new ArppFyProjectUpdatePdfBuilder().Build(report, currentFyOptions);
        Assert.True(pdf.Length > 1000);
        Assert.Equal((byte)'%', pdf[0]);
    }

    [Fact]
    public void Production_hardening_preserves_long_arpp_number_and_marks_completed_pdc()
    {
        const string longArppNumber = "ARPP/IR&D/CF/VR/2026-27/123";
        var report = SampleReportWithCompletedProject(longArppNumber);

        var word = new ArppFyProjectUpdateWordBuilder().Build(report);
        using (var stream = new MemoryStream(word))
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
            Assert.Contains(longArppNumber, text, StringComparison.Ordinal);
            Assert.Contains("Completed", text, StringComparison.Ordinal);
        }

        var excel = new ArppFyProjectUpdateExcelBuilder().Build(report);
        using (var excelStream = new MemoryStream(excel))
        using (var workbook = new XLWorkbook(excelStream))
        {
            var worksheet = workbook.Worksheet("ARPP Project Update");
            Assert.Equal(longArppNumber, worksheet.Cell(5, 2).GetString());
            Assert.Equal("Completed", worksheet.Cell(5, 10).GetString());
            Assert.InRange(worksheet.Column(2).Width, 17.99d, 18.01d);
        }

        var excelWithStage = new ArppFyProjectUpdateExcelBuilder().Build(
            report,
            new ArppFyProjectUpdatePresentationOptions(IncludePresentStage: true));
        using (var excelStream = new MemoryStream(excelWithStage))
        using (var workbook = new XLWorkbook(excelStream))
        {
            var worksheet = workbook.Worksheet("ARPP Project Update");
            Assert.Equal("Completed", worksheet.Cell(5, 9).GetString());
            Assert.Equal("Completed", worksheet.Cell(5, 11).GetString());
        }

        var pdfSource = ReadRepoFile(
            "Services",
            "Reports",
            "ArppFyProjectUpdate",
            "ArppFyProjectUpdatePdfBuilder.cs");
        Assert.Contains("BodyCell(table.Cell(), Pdc(row), center: true);", pdfSource, StringComparison.Ordinal);
        Assert.Contains("columns.RelativeColumn(1.25f);", pdfSource, StringComparison.Ordinal);
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
                {
                    CurrentFyArppListingDate = new DateOnly(2026, 7, 27)
                }
            },
            Array.Empty<ArppFyReportWarning>(),
            0);

    private static ArppFyProjectUpdateReport SampleReportWithCompletedProject(string pppNumber)
        => new(
            2026,
            new DateTimeOffset(2026, 8, 17, 3, 0, 0, TimeSpan.Zero),
            new[]
            {
                new ArppFyProjectUpdateRow(
                    1,
                    20,
                    pppNumber,
                    "Completed Production Simulator",
                    new DateOnly(2024, 12, 31),
                    "9.3",
                    "Comdt SDD",
                    "SDD",
                    new DateOnly(2025, 11, 18),
                    900_000m,
                    ProjectSupplyOrderValueBasis.Pnc,
                    new DateOnly(2026, 1, 29),
                    new DateOnly(2025, 12, 31), // historical PDC must not leak into the report
                    ArppCategory.CarryForward,
                    "Project Completed. Available for proliferation.",
                    new DateOnly(2026, 8, 16),
                    ProjectLifecycleStatus.Completed,
                    false,
                    "DEVP",
                    "Development",
                    ProjectStageMaturityOrder.Completed)
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
