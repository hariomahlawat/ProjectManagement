namespace ProjectManagement.Services.Arpp;

public interface IArppExportService
{
    ArppExportFile BuildExcel(
        ArppIssueDetails issue,
        DateTimeOffset generatedAtUtc,
        bool includeRecordControlMetadata = true,
        bool includePrismLinkageColumns = true);
}
