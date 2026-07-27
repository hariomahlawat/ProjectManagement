namespace ProjectManagement.Services.Arpp;

/// <summary>
/// Synchronizes the project IPA lifecycle stage from verified, published ARPP records.
/// Published ARPP entries are the only authority used by this service.
/// </summary>
public interface IArppIpaStageSynchronizer
{
    Task<ArppIpaStageSynchronizationResult> SynchronizeProjectsAsync(
        IEnumerable<int> projectIds,
        CancellationToken cancellationToken = default);

    Task<ArppIpaStageSynchronizationResult> SynchronizeAllAsync(
        CancellationToken cancellationToken = default);
}
