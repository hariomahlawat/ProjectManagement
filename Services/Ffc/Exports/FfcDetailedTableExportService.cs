using System;
using System.Globalization;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Ffc.Exports;

public sealed class FfcDetailedTableExportService : IFfcDetailedTableExportService
{
    private readonly FfcDetailedWordDocumentBuilder _wordBuilder;
    private readonly FfcDetailedExcelWorkbookBuilder _excelBuilder;

    public FfcDetailedTableExportService(
        FfcDetailedWordDocumentBuilder wordBuilder,
        FfcDetailedExcelWorkbookBuilder excelBuilder)
    {
        _wordBuilder = wordBuilder ?? throw new ArgumentNullException(nameof(wordBuilder));
        _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
    }

    public FfcDetailedTableExportFile BuildWord(FfcDetailedTableExportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());
        return new FfcDetailedTableExportFile(
            _wordBuilder.Build(context),
            FfcDetailedTableExportDefaults.WordContentType,
            BuildFileName(generatedAtIst, "docx"));
    }

    public FfcDetailedTableExportFile BuildExcel(FfcDetailedTableExportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());
        return new FfcDetailedTableExportFile(
            _excelBuilder.Build(context),
            FfcDetailedTableExportDefaults.ExcelContentType,
            BuildFileName(generatedAtIst, "xlsx"));
    }

    private static string BuildFileName(DateTimeOffset generatedAtIst, string extension)
        => string.Format(
            CultureInfo.InvariantCulture,
            "FFC_Projects_Update_{0:yyyyMMdd_HHmm}.{1}",
            generatedAtIst,
            extension);
}
