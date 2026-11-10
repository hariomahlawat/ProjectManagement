using ProjectManagement.Data;
using ProjectManagement.Services.ItProjects;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// What the workspace rail shows an officer who holds the ITO role: the number of live IT projects
/// (the ITO runs all of them; see <see cref="LiveItProjects"/>).
/// </summary>
public interface IItoWorkspaceQuery
{
    Task<int> CountLiveItProjectsAsync(CancellationToken cancellationToken = default);
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
}
