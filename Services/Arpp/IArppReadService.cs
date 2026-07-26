namespace ProjectManagement.Services.Arpp;

public interface IArppReadService
{
    Task<ArppRegisterResult> GetRegisterAsync(
        int? financialYearStart,
        string? query,
        CancellationToken cancellationToken = default);

    Task<ArppIssueDetails?> GetIssueAsync(
        long issueId,
        CancellationToken cancellationToken = default);

    Task<ArppProjectHistory?> GetProjectHistoryAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<int> GetSuggestedIssueSequenceAsync(
        int financialYearStart,
        CancellationToken cancellationToken = default);

    Task<bool> HasOriginalIssueAsync(
        int financialYearStart,
        CancellationToken cancellationToken = default);
}
