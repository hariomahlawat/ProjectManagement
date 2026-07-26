using System.Globalization;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Arpp;

public sealed class ArppExportService : IArppExportService
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly ArppExcelWorkbookBuilder _workbookBuilder;

    public ArppExportService(ArppExcelWorkbookBuilder workbookBuilder)
    {
        _workbookBuilder = workbookBuilder ?? throw new ArgumentNullException(nameof(workbookBuilder));
    }

    public ArppExportFile BuildExcel(
        ArppIssueDetails issue,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var financialYear = FinancialYearHelper.Format(issue.FinancialYearStart).Replace('-', '_');
        var issueLabel = issue.IssueSequence == 0
            ? "Original"
            : $"Addendum_{issue.IssueSequence.ToString(CultureInfo.InvariantCulture)}";
        var fileName = $"ARPP_{financialYear}_{issueLabel}_{generatedAtUtc:yyyyMMdd_HHmm}.xlsx";

        return new ArppExportFile(
            _workbookBuilder.Build(issue, generatedAtUtc),
            ExcelContentType,
            fileName);
    }
}
