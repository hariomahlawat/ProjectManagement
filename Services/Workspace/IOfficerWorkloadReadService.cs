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

    Task<CommandOfficerWorkloadVm?> GetOfficerAsync(
        string officerUserId,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default);
}
