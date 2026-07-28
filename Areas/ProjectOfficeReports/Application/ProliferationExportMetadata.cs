namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed record ProliferationChronologyQualitySummary(
    int ApprovedRecordCount,
    int AffectedPositionCount,
    int ReportedQuantity,
    int MinimumValidYear,
    int MaximumValidYear)
{
    public bool HasIssues => ApprovedRecordCount > 0 || ReportedQuantity != 0;
}

public sealed record ProliferationExportMetadata(
    DateTimeOffset GeneratedAtUtc,
    string GeneratedBy,
    ProliferationChronologyQualitySummary ChronologyQuality,
    string DataQualityMessage,
    bool IncludesUnitSummary = false);
