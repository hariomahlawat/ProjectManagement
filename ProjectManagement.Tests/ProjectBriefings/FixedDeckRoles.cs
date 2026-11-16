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
}
