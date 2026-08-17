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

/// <summary>
/// Selects which authoritative ARPP date is rendered in the
/// "Dt of Grant of IPA / ARPP Listing" report column.
/// </summary>
public enum ArppListingDateMode
{
    /// <summary>
    /// First authoritative published ARPP listing across all financial years.
    /// This preserves the report's original behaviour.
    /// </summary>
    InitialListing = 0,

    /// <summary>
    /// Issue date of the published Original ARPP / Addendum row that determines
    /// the project's current position in the selected financial year.
    /// </summary>
    CurrentFinancialYear = 1
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
    /// <summary>
    /// Issue date of the selected FY's authoritative published row for this
    /// project's current ARPP position. If an addendum establishes the current
    /// position, this is the addendum issue date.
    /// </summary>
    public DateOnly? CurrentFyArppListingDate { get; init; }

    public decimal? SupplyOrderAmountInCrores => SupplyOrderAmountInRupees is > 0m
        ? SupplyOrderAmountInRupees.Value / 10_000_000m
        : null;

    public string ProjectCaseDisplay => ArppDisplayNames.For(ProjectCase);

    /// <summary>
    /// Explicit project completion is authoritative for report presentation.
    /// Consumers must not infer completion from historical stage records.
    /// </summary>
    public bool IsCompleted => LifecycleStatus == ProjectLifecycleStatus.Completed;

    public string StageDisplay => IsCompleted
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

public sealed record ArppFyProjectUpdatePresentationOptions(
    bool IncludePresentStage = false,
    ArppListingDateMode ListingDateMode = ArppListingDateMode.InitialListing)
{
    public static ArppFyProjectUpdatePresentationOptions Default { get; } = new();

    public int ColumnCount => IncludePresentStage ? 13 : 12;
    public int StatusColumnCount => IncludePresentStage ? 4 : 3;

    public ArppListingDateMode EffectiveListingDateMode =>
        NormalizeListingDateMode(ListingDateMode);

    public DateOnly? ResolveListingDate(ArppFyProjectUpdateRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return EffectiveListingDateMode == ArppListingDateMode.CurrentFinancialYear
            ? row.CurrentFyArppListingDate
            : row.FirstArppListingDate;
    }

    public static ArppListingDateMode NormalizeListingDateMode(ArppListingDateMode mode)
        => mode == ArppListingDateMode.CurrentFinancialYear
            ? ArppListingDateMode.CurrentFinancialYear
            : ArppListingDateMode.InitialListing;
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
    ArppFyProjectUpdateFile BuildWord(
        ArppFyProjectUpdateReport report,
        ArppFyProjectUpdatePresentationOptions? options = null);

    ArppFyProjectUpdateFile BuildPdf(
        ArppFyProjectUpdateReport report,
        ArppFyProjectUpdatePresentationOptions? options = null);

    ArppFyProjectUpdateFile BuildExcel(
        ArppFyProjectUpdateReport report,
        ArppFyProjectUpdatePresentationOptions? options = null);
}
