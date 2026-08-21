using System;
using System.Collections.Generic;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;

public sealed record ProliferationSummaryViewModel(
    IReadOnlyList<ProliferationSummaryProjectRow> ByProject,
    IReadOnlyList<ProliferationSummaryYearRow> ByYear,
    IReadOnlyList<ProliferationSummaryProjectYearRow> ByProjectYear)
{
    public static ProliferationSummaryViewModel Empty { get; } = new(
        Array.Empty<ProliferationSummaryProjectRow>(),
        Array.Empty<ProliferationSummaryYearRow>(),
        Array.Empty<ProliferationSummaryProjectYearRow>());
}

public sealed record ProliferationSummaryProjectRow(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    ProliferationSummarySourceTotals Totals);

public sealed record ProliferationSummaryYearRow(
    int Year,
    ProliferationSummarySourceTotals Totals);

public sealed record ProliferationSummaryProjectYearRow(
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    int Year,
    ProliferationSummarySourceTotals Totals);

public sealed record ProliferationSummarySourceTotals(
    int Total,
    int Sdd,
    int Abw515);

public sealed record TechnicalCategoryBreakdownRow(
    int? TechnicalCategoryId,
    string Name,
    int Total);


public sealed record ProliferationOperationalSnapshot(
    IReadOnlyList<RecentProliferationRow> RecentProliferation,
    ProliferationStaffActivitySummary StaffActivity)
{
    public static ProliferationOperationalSnapshot Empty { get; } = new(
        Array.Empty<RecentProliferationRow>(),
        ProliferationStaffActivitySummary.Empty);
}

public sealed record RecentProliferationRow(
    ProliferationRecordKind Kind,
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    ProliferationSource Source,
    DateOnly? ProliferationDate,
    int Year,
    string? UnitName,
    int Quantity,
    int RecordCount,
    int ReceivingUnitCount,
    DateTime CreatedOnUtc,
    DateTime LastUpdatedOnUtc,
    int? EntryDelayDays)
{
    public string SourceLabel => Source.ToDisplayName();

    public string RecordTypeLabel => Kind switch
    {
        ProliferationRecordKind.Granular when RecordCount > 1 => $"{RecordCount:N0} detailed entries",
        ProliferationRecordKind.Granular => "Detailed entry",
        _ => "Annual quantity"
    };

    public string BusinessDateLabel =>
        ProliferationDate.HasValue
            ? ProliferationDate.Value.ToString("dd MMM yyyy")
            : $"Annual · {Year}";

    public string? ReceivingUnitLabel => Kind switch
    {
        ProliferationRecordKind.Granular when ReceivingUnitCount > 1 => $"{ReceivingUnitCount:N0} receiving units",
        ProliferationRecordKind.Granular when ReceivingUnitCount == 1 && !string.IsNullOrWhiteSpace(UnitName) => UnitName,
        ProliferationRecordKind.Granular when ReceivingUnitCount == 1 => "1 receiving unit",
        _ => null
    };
}

public sealed record ProliferationStaffActivitySummary(
    DateTime? LatestActivityUtc,
    DateTime? LatestDataEntryUtc,
    int ActionsLast30Days,
    int ActiveStaffLast30Days,
    IReadOnlyList<ProliferationStaffActivityRow> RecentActivity)
{
    public static ProliferationStaffActivitySummary Empty { get; } = new(
        null,
        null,
        0,
        0,
        Array.Empty<ProliferationStaffActivityRow>());
}

public sealed record ProliferationStaffActivityRow(
    long AuditId,
    DateTime TimeUtc,
    string ActionLabel,
    int ActionCount,
    string? ActorDisplayName,
    int? ProjectId,
    string? ProjectName,
    string? ProjectCode,
    string? SourceLabel,
    string? RecordReference);
