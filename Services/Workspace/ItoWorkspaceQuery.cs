using ProjectManagement.Data;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// What the workspace rail shows about the projects of the IT projects engine: for an officer who
/// holds the ITO role, the number of live IT projects (the ITO runs all of them); for a Project
/// Officer, the MOLTI and SRE modules in which they have live projects assigned
/// (see <see cref="LiveItProjects"/>).
/// </summary>
public interface IItoWorkspaceQuery
{
    Task<int> CountLiveItProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A link per module in which this user has live projects assigned, in catalogue order, with
    /// the count. Empty for a user with none (and for a user who can no longer be an officer).
    /// </summary>
    Task<IReadOnlyList<WorkspaceModuleLinkVm>> GetAssignedModuleLinksAsync(string? userId, CancellationToken cancellationToken = default);
}

public sealed class ItoWorkspaceQuery : IItoWorkspaceQuery
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ItoWorkspaceQuery(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<int> CountLiveItProjectsAsync(CancellationToken cancellationToken = default)
        => (await LiveItProjects.LoadAsync(_db, _clock.UtcNow, cancellationToken)).Count;

    public async Task<IReadOnlyList<WorkspaceModuleLinkVm>> GetAssignedModuleLinksAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<WorkspaceModuleLinkVm>();
        }

        var mine = (await LiveItProjects.LoadAssignedAsync(_db, _clock.UtcNow, cancellationToken))
            .Where(row => string.Equals(row.ProjectOfficerUserId, userId, StringComparison.Ordinal))
            .ToList();
        return LiveItProjects.OfficerModules
            .Select(module => new WorkspaceModuleLinkVm(
                module.Name,
                module.PageFolder + "/Index",
                LiveItProjects.IconOf(module.Code),
                mine.Count(row => row.Module == module.Code)))
            .Where(link => link.Count > 0)
            .ToList();
    }
}
