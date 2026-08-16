using System.Globalization;

namespace ProjectManagement.Services.Reports.ArppFyProjectUpdate;

public sealed class ArppFyProjectUpdateExportService : IArppFyProjectUpdateExportService
{
    private const string WordContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PdfContentType = "application/pdf";
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ArppFyProjectUpdateWordBuilder _wordBuilder;
    private readonly ArppFyProjectUpdatePdfBuilder _pdfBuilder;
    private readonly ArppFyProjectUpdateExcelBuilder _excelBuilder;

    public ArppFyProjectUpdateExportService(
        ArppFyProjectUpdateWordBuilder wordBuilder,
        ArppFyProjectUpdatePdfBuilder pdfBuilder,
        ArppFyProjectUpdateExcelBuilder excelBuilder)
    {
        _wordBuilder = wordBuilder ?? throw new ArgumentNullException(nameof(wordBuilder));
        _pdfBuilder = pdfBuilder ?? throw new ArgumentNullException(nameof(pdfBuilder));
        _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
    }

    public ArppFyProjectUpdateFile BuildWord(ArppFyProjectUpdateReport report)
        => Build(report, "docx", WordContentType, _wordBuilder.Build(report));

    public ArppFyProjectUpdateFile BuildPdf(ArppFyProjectUpdateReport report)
        => Build(report, "pdf", PdfContentType, _pdfBuilder.Build(report));

    public ArppFyProjectUpdateFile BuildExcel(ArppFyProjectUpdateReport report)
        => Build(report, "xlsx", ExcelContentType, _excelBuilder.Build(report));

    private static ArppFyProjectUpdateFile Build(
        ArppFyProjectUpdateReport report,
        string extension,
        string contentType,
        byte[] content)
    {
        ArgumentNullException.ThrowIfNull(report);
        var fy = report.FinancialYearDisplay.Replace('-', '_');
        var stamp = report.GeneratedAtUtc.ToUniversalTime().ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        var fileName = $"ARPP_Project_Update_FY_{fy}_{stamp}.{extension}";
        return new ArppFyProjectUpdateFile(content, contentType, fileName);
    }
}
