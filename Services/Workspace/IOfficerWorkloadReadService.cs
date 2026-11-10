using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Provides the canonical, read-only composition of Project Officer workload used by
/// command and individual workspaces. The service owns assignment filtering, lifecycle
/// filtering, stage resolution and officer ordering so consumers cannot drift.
/// </summary>
public interface IOfficerWorkloadReadService
{
    Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The Officer workload view's cards: <see cref="GetAllAsync"/> plus, for every active officer who
    /// holds the ITO role, the live IT projects (IT projects have no assigned Project Officer; the ITO
    /// runs them). An ITO with no other workload gets a card of their own. Other consumers keep using
    /// <see cref="GetAllAsync"/>, whose scope is unchanged.
    /// </summary>
    Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllWithItProjectsAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default);

    Task<CommandOfficerWorkloadVm?> GetOfficerAsync(
        string officerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Officers with workload on the Officer workload view, ITO cards included.</summary>
    Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default);
}
