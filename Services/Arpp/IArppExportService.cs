namespace ProjectManagement.Services.Arpp;

public interface IArppExportService
{
    ArppExportFile BuildExcel(
        ArppIssueDetails issue,
        DateTimeOffset generatedAtUtc);
}
