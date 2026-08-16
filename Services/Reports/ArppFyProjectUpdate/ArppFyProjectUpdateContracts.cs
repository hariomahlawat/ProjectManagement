using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Reports.ArppFyProjectUpdate;

public enum ArppFyReportWarningSeverity
{
    Info = 0,
    Warning = 1
}

public sealed record ArppFyReportWarning(
    string Code,
    ArppFyReportWarningSeverity Severity,
    string Message,
    int? ProjectId = null,
    string? ProjectName = null);

public sealed record ArppFyProjectUpdateRow(
    int SerialNumber,
    int ProjectId,
    string? PppNumber,
    string ProjectName,
    DateOnly? FirstArppListingDate,
    string? DfpdsSchedule,
    string? Cfa,
    string Establishment,
    DateOnly? AonDate,
    decimal? SupplyOrderAmountInRupees,
    ProjectSupplyOrderValueBasis SupplyOrderAmountBasis,
    DateOnly? SupplyOrderDate,
    DateOnly? DevelopmentPdcDate,
    ArppCategory ProjectCase,
    string? LatestExternalRemark,
    DateOnly? LatestExternalRemarkEventDate,
    ProjectLifecycleStatus LifecycleStatus,
    bool IsArchived,
    string? CurrentStageCode,
    string CurrentStageLabel,
    int StageOrder)
{
    public decimal? SupplyOrderAmountInCrores => SupplyOrderAmountInRupees is > 0m
        ? SupplyOrderAmountInRupees.Value / 10_000_000m
        : null;

    public string ProjectCaseDisplay => ArppDisplayNames.For(ProjectCase);

    public string StageDisplay => LifecycleStatus == ProjectLifecycleStatus.Completed
        ? "Completed"
        : CurrentStageLabel;
}

public sealed record ArppFyProjectUpdateReport(
    int FinancialYearStart,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ArppFyProjectUpdateRow> Rows,
    IReadOnlyList<ArppFyReportWarning> Warnings,
    int UnlinkedPublishedRowCount)
{
    public string FinancialYearDisplay => FinancialYearHelper.Format(FinancialYearStart);
    public string FormalTitle => $"PROJECT UPDATE : ARPP LISTED PROJECTS (FY {FinancialYearDisplay})";
    public int CompletedCount => Rows.Count(row => row.LifecycleStatus == ProjectLifecycleStatus.Completed);
    public int OngoingCount => Rows.Count - CompletedCount;
    public int WarningCount => Warnings.Count(warning => warning.Severity == ArppFyReportWarningSeverity.Warning);
    public bool CanExport => Rows.Count > 0;
}

public sealed record ArppFyProjectUpdateFile(
    byte[] Content,
    string ContentType,
    string FileName);

public interface IArppFyProjectUpdateService
{
    Task<IReadOnlyList<int>> GetAvailableFinancialYearsAsync(
        CancellationToken cancellationToken = default);

    Task<ArppFyProjectUpdateReport?> BuildAsync(
        int financialYearStart,
        CancellationToken cancellationToken = default);
}

public interface IArppFyProjectUpdateExportService
{
    ArppFyProjectUpdateFile BuildWord(ArppFyProjectUpdateReport report);
    ArppFyProjectUpdateFile BuildPdf(ArppFyProjectUpdateReport report);
    ArppFyProjectUpdateFile BuildExcel(ArppFyProjectUpdateReport report);
}
