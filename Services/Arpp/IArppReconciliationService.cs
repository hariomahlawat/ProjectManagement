namespace ProjectManagement.Services.Arpp;

public interface IArppReconciliationService
{
    Task<ArppReconciliationResult> GetQueueAsync(
        int? financialYearStart,
        string? query,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<ArppCommandResult> LinkAsync(
        ArppReconciliationCommand command,
        CancellationToken cancellationToken = default);
}
