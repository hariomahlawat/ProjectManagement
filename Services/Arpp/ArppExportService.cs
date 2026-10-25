using ProjectManagement.Models.Arpp;
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
        DateTimeOffset generatedAtUtc,
        bool includeRecordControlMetadata = true,
        bool includePrismLinkageColumns = true)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var financialYear = FinancialYearHelper.Format(issue.FinancialYearStart).Replace('-', '_');
        var issueLabel = ArppDisplayNames.IssueFileLabel(issue.Kind, issue.IssueSequence);
        var fileName = $"ARPP_{financialYear}_{issueLabel}_{generatedAtUtc:yyyyMMdd_HHmm}.xlsx";

        return new ArppExportFile(
            _workbookBuilder.Build(
                issue,
                generatedAtUtc,
                includeRecordControlMetadata,
                includePrismLinkageColumns),
            ExcelContentType,
            fileName);
    }
}
