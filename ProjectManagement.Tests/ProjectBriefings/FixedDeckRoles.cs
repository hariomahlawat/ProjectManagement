using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Tests.ProjectBriefings;

// A role answer for deck tests that do not seed Identity roles: everyone is command (Comdt or HoD),
// as every deck user was before Project Officers had decks, or nobody is.
public sealed class FixedDeckRoles : IProjectBriefingDeckRoleResolver
{
    private readonly Func<string, bool> _isCommand;

    public FixedDeckRoles(Func<string, bool> isCommand)
    {
        _isCommand = isCommand;
    }

    public static FixedDeckRoles Command { get; } = new(_ => true);

    public Task<bool> IsCommandAsync(string userId, CancellationToken cancellationToken = default)
        => Task.FromResult(_isCommand(userId));

    // A command user here is an HoD; anyone else a Project Officer.
    public Task<IReadOnlyCollection<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<string>>(_isCommand(userId)
            ? new[] { ProjectManagement.Configuration.RoleNames.HoD }
            : new[] { ProjectManagement.Configuration.RoleNames.ProjectOfficer });
}
